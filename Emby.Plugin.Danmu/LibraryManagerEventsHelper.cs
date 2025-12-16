using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.Danmu.Core;
using Emby.Plugin.Danmu.Model;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using Microsoft.Extensions.Caching.Memory;
using Emby.Plugin.Danmu.Scrapers;
using Emby.Plugin.Danmu.Core.Extensions;
using Emby.Plugin.Danmu.Configuration;

namespace Emby.Plugin.Danmu;

public class LibraryManagerEventsHelper : IDisposable
{
    private readonly List<LibraryEvent> _queuedEvents;
    private readonly IMemoryCache _memoryCache;
    private readonly MemoryCacheEntryOptions _pendingAddExpiredOption = new MemoryCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) };
    private readonly MemoryCacheEntryOptions _danmuUpdatedExpiredOption = new MemoryCacheEntryOptions() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;
    private readonly Emby.Plugin.Danmu.Core.IFileSystem _fileSystem;
    private Timer _queueTimer;
    private ScraperManager _scraperManager; // 改为非 readonly，允许在运行时切换实例

    public PluginConfiguration Config
    {
        get
        {
            return Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        }
    }


    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryManagerEventsHelper"/> class.
    /// </summary>
    /// <param name="libraryManager">The <see cref="ILibraryManager"/>.</param>
    /// <param name="logManager">The <see cref="ILogManager"/>.</param>
    /// <param name="scraperManager">The <see cref="ScraperManager"/>.</param>
    public LibraryManagerEventsHelper(ILibraryManager libraryManager, ILogManager logManager, ScraperManager scraperManager)
    {
        _queuedEvents = new List<LibraryEvent>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _libraryManager = libraryManager;
        _logger = logManager.GetLogger(GetType().Name);
        _fileSystem = Core.FileSystem.instant;

        // 优先使用 Plugin 中已注册弹幕源的实例
        _scraperManager = Core.SingletonManager.ScraperManager ?? scraperManager;

        // 如果注入实例为空或未注册任何弹幕源，尝试切换到 SingletonManager 中的实例
        if ((_scraperManager?.AllWithNoEnabled()?.Count ?? 0) == 0 && Core.SingletonManager.ScraperManager != null)
        {
            _logger.Warn("注入的 ScraperManager 未注册弹幕源，改用 SingletonManager 中的实例");
            _scraperManager = Core.SingletonManager.ScraperManager;
        }

        _logger.Info("LibraryManagerEventsHelper 初始化: ScraperManager 内部注册数量={0}",
            _scraperManager?.AllWithNoEnabled()?.Count ?? 0);
    }

    /// <summary>
    /// 确保 _scraperManager 指向已注册弹幕源的实例
    /// </summary>
    private ScraperManager EnsureScraperManagerReady()
    {
        if (_scraperManager == null || (_scraperManager.AllWithNoEnabled()?.Count ?? 0) == 0)
        {
            var singletonSm = Core.SingletonManager.ScraperManager;
            if (singletonSm != null && (singletonSm.AllWithNoEnabled()?.Count ?? 0) > 0)
            {
                _logger.Warn("当前 ScraperManager 未注册弹幕源，切换到 SingletonManager 中的实例");
                _scraperManager = singletonSm;
            }
        }

        return _scraperManager;
    }

    /// <summary>
    /// Queues an item to be added to trakt.
    /// </summary>
    /// <param name="item"> The <see cref="BaseItem"/>.</param>
    /// <param name="eventType">The <see cref="EventType"/>.</param>
    public void QueueItem(BaseItem item, EventType eventType)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        // 确保使用已注册弹幕源的 ScraperManager
        EnsureScraperManagerReady();

        // 对于强制事件(手动搜索触发)，立即同步处理，不等待定时器
        if (eventType == EventType.Force)
        {
            _logger.Info("立即处理强制下载任务: {0} (ItemId={1})", item.Name, item.Id);
            var libraryEvent = new LibraryEvent { Item = item, EventType = eventType };
            
            // 同步处理，直接等待完成
            try
            {
                _logger.Info("强制下载任务处理开始: {0} (ItemId={1})", item.Name, item.Id);
                
                if (item is Movie)
                {
                    _logger.Info("开始处理电影强制下载任务: {0}", item.Name);
                    // 使用 GetAwaiter().GetResult() 同步等待异步方法
                    ProcessQueuedMovieEvents(new[] { libraryEvent }, EventType.Force).GetAwaiter().GetResult();
                    _logger.Info("电影强制下载任务处理完成: {0}", item.Name);
                }
                else if (item is Episode)
                {
                    _logger.Info("开始处理剧集强制下载任务: {0}", item.Name);
                    // 使用 GetAwaiter().GetResult() 同步等待异步方法
                    ProcessQueuedEpisodeEvents(new[] { libraryEvent }, EventType.Force).GetAwaiter().GetResult();
                    _logger.Info("剧集强制下载任务处理完成: {0}", item.Name);
                }
                else
                {
                    _logger.Warn("不支持的项目类型，跳过处理: {0} (Type={1})", item.Name, item.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogErrorException("处理强制下载任务时发生异常: {0}", ex, item.Name);
            }
            
            return;
        }

        // 对于其他事件类型(Add, Update)，使用现有的延迟队列逻辑
        lock (_queuedEvents)
        {
            var libraryEvent = new LibraryEvent { Item = item, EventType = eventType };
            
            // 检查队列中是否已存在相同的事件
            if (_queuedEvents.Contains(libraryEvent))
            {
                _logger.Debug("事件已在队列中,忽略重复添加: {ItemName} ({EventType})", item.Name, eventType);
                return;
            }

            if (_queueTimer == null)
            {
                _queueTimer = new Timer(
                    OnQueueTimerCallback,
                    null,
                    TimeSpan.FromMilliseconds(10000),
                    Timeout.InfiniteTimeSpan);
            }
            else
            {
                _queueTimer.Change(TimeSpan.FromMilliseconds(10000), Timeout.InfiniteTimeSpan);
            }

            _queuedEvents.Add(libraryEvent);
        }
    }

    /// <summary>
    /// Wait for timer callback to be completed.
    /// </summary>
    private async void OnQueueTimerCallback(object state)
    {
        try
        {
            await OnQueueTimerCallbackInternal().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogErrorException("Error in OnQueueTimerCallbackInternal", ex);
        }
    }

    /// <summary>
    /// Wait for timer to be completed.
    /// </summary>
    private async Task OnQueueTimerCallbackInternal()
    {
        // _logger.Info("Timer elapsed - processing queued items");
        List<LibraryEvent> queue;

        lock (_queuedEvents)
        {
            if (!_queuedEvents.Any())
            {
                _logger.Info("No events... stopping queue timer");
                return;
            }

            queue = _queuedEvents.ToList();
            _queuedEvents.Clear();
        }

        var queuedMovieAdds = new List<LibraryEvent>();
        var queuedMovieUpdates = new List<LibraryEvent>();
        var queuedMovieForces = new List<LibraryEvent>();
        var queuedEpisodeAdds = new List<LibraryEvent>();
        var queuedEpisodeUpdates = new List<LibraryEvent>();
        var queuedEpisodeForces = new List<LibraryEvent>();
        var queuedShowAdds = new List<LibraryEvent>();
        var queuedShowUpdates = new List<LibraryEvent>();
        var queuedSeasonAdds = new List<LibraryEvent>();
        var queuedSeasonUpdates = new List<LibraryEvent>();

        // add事件可能会在获取元数据完之前执行，导致可能会中断元数据获取，通过pending集合把add事件延缓到获取元数据后再执行（获取完元数据后，一般会多推送一个update事件）
        foreach (var ev in queue)
        {

            // item所在的媒体库不启用弹幕插件，忽略处理
            if (IsIgnoreItem(ev.Item))
            {
                continue;
            }


            switch (ev.Item)
            {
                case Movie when ev.EventType is EventType.Add:
                    _logger.Info("Movie add: {0}", ev.Item.Name);
                    _memoryCache.Set<LibraryEvent>(ev.Item.Id, ev, _pendingAddExpiredOption);
                    break;
                case Movie when ev.EventType is EventType.Update:
                    _logger.Info("Movie update: {0}", ev.Item.Name);
                    if (_memoryCache.TryGetValue<LibraryEvent>(ev.Item.Id, out LibraryEvent addMovieEv))
                    {
                        queuedMovieAdds.Add(addMovieEv);
                        _memoryCache.Remove(ev.Item.Id);
                    }
                    else
                    {
                        queuedMovieUpdates.Add(ev);
                    }
                    break;
                case Movie when ev.EventType is EventType.Force:
                    _logger.Info("Movie force: {0}", ev.Item.Name);
                    queuedMovieForces.Add(ev);
                    break;
                case Series when ev.EventType is EventType.Add:
                    _logger.Info("Series add: {0}", ev.Item.Name);
                    // _pendingAddEventCache.Set<LibraryEvent>(ev.Item.Id, ev, _expiredOption);
                    break;
                case Series when ev.EventType is EventType.Update:
                    _logger.Info("Series update: {0}", ev.Item.Name);
                    // if (_pendingAddEventCache.TryGetValue<LibraryEvent>(ev.Item.Id, out LibraryEvent addSerieEv))
                    // {
                    //     // 紧跟add事件的update事件不需要处理
                    //     _pendingAddEventCache.Remove(ev.Item.Id);
                    // }
                    // else
                    // {
                    //     queuedShowUpdates.Add(ev);
                    // }
                    break;
                case Season when ev.EventType is EventType.Add:
                    _logger.Info("Season add: {0}", ev.Item.Name);
                    _memoryCache.Set<LibraryEvent>(ev.Item.Id, ev, _pendingAddExpiredOption);
                    break;
                case Season when ev.EventType is EventType.Update:
                    _logger.Info("Season update: {0}", ev.Item.Name);
                    if (_memoryCache.TryGetValue<LibraryEvent>(ev.Item.Id, out LibraryEvent addSeasonEv))
                    {
                        queuedSeasonAdds.Add(addSeasonEv);
                        _memoryCache.Remove(ev.Item.Id);
                    }
                    else
                    {
                        queuedSeasonUpdates.Add(ev);
                    }
                    break;
                case Episode when ev.EventType is EventType.Update:
                    _logger.Info("Episode update: {0}.{1}", ev.Item.IndexNumber, ev.Item.Name);
                    queuedEpisodeUpdates.Add(ev);
                    break;
                case Episode when ev.EventType is EventType.Force:
                    _logger.Info("Episode force: {0}.{1}", ev.Item.IndexNumber, ev.Item.Name);
                    queuedEpisodeForces.Add(ev);
                    break;
            }

        }

        // 对于剧集，处理顺序也很重要（Add事件后，会刷新元数据，导致会同时推送Update事件）
        await ProcessQueuedMovieEvents(queuedMovieAdds, EventType.Add).ConfigureAwait(false);
        await ProcessQueuedMovieEvents(queuedMovieUpdates, EventType.Update).ConfigureAwait(false);

        await ProcessQueuedShowEvents(queuedShowAdds, EventType.Add).ConfigureAwait(false);
        await ProcessQueuedSeasonEvents(queuedSeasonAdds, EventType.Add).ConfigureAwait(false);
        await ProcessQueuedEpisodeEvents(queuedEpisodeAdds, EventType.Add).ConfigureAwait(false);

        await ProcessQueuedShowEvents(queuedShowUpdates, EventType.Update).ConfigureAwait(false);
        await ProcessQueuedSeasonEvents(queuedSeasonUpdates, EventType.Update).ConfigureAwait(false);
        await ProcessQueuedEpisodeEvents(queuedEpisodeUpdates, EventType.Update).ConfigureAwait(false);

        await ProcessQueuedMovieEvents(queuedMovieForces, EventType.Force).ConfigureAwait(false);
        await ProcessQueuedEpisodeEvents(queuedEpisodeForces, EventType.Force).ConfigureAwait(false);
    }

    public bool IsIgnoreItem(BaseItem item)
    {
        // item所在的媒体库不启用弹幕插件，忽略处理
        var libraryOptions = _libraryManager.GetLibraryOptions(item);
        if (libraryOptions != null && libraryOptions.DisabledSubtitleFetchers.Contains(Plugin.Instance?.Name))
        {
            this._logger.LogInformation($"媒体库已关闭danmu插件, 忽略处理[{item.Name}].");
            return true;
        }

        return false;
    }


    /// <summary>
    /// Processes queued movie events.
    /// </summary>
    /// <param name="events">The <see cref="LibraryEvent"/> enumerable.</param>
    /// <param name="eventType">The <see cref="EventType"/>.</param>
    /// <returns>Task.</returns>
    public async Task ProcessQueuedMovieEvents(IReadOnlyCollection<LibraryEvent> events, EventType eventType)
    {
        if (events.Count == 0)
        {
            return;
        }

        _logger.Debug("Processing {Count} movies with event type {EventType}", events.Count, eventType);

        var movies = events.Select(lev => (Movie)lev.Item)
            .Where(lev => !string.IsNullOrEmpty(lev.Name))
            .ToHashSet();


        // 新增事件也会触发update，不需要处理Add
        // 更新，判断是否有bvid，有的话刷新弹幕文件
        if (eventType == EventType.Add)
        {
            var queueUpdateMeta = new List<BaseItem>();
            foreach (var item in movies)
            {
                foreach (var scraper in _scraperManager.All())
                {
                    try
                    {
                        // 读取最新数据，要不然取不到年份信息
                        var currentItem = _libraryManager.GetItemById(item.Id) ?? item;

                        var mediaId = await scraper.SearchMediaId(currentItem).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(mediaId))
                        {
                            _logger.Info("[{0}]元数据匹配失败：{1} ({2})", scraper.Name, item.Name, item.ProductionYear);
                            continue;
                        }

                        var media = await scraper.GetMedia(item, mediaId);
                        if (media != null)
                        {
                            var providerVal = media.Id;
                            var commentId = media.CommentId;
                            _logger.Info("[{0}]匹配成功：name={1} ProviderId: {2}", scraper.Name, item.Name, providerVal);

                            // 更新epid元数据
                            item.SetProviderId(scraper.ProviderId, providerVal);
                            queueUpdateMeta.Add(item);

                            // 下载弹幕
                            await this.DownloadDanmu(scraper, item, commentId).ConfigureAwait(false);
                            break;
                        }
                    }
                    catch (FrequentlyRequestException ex)
                    {
                        _logger.LogErrorException("[{0}]api接口触发风控，中止执行，请稍候再试.", scraper.Name, ex);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogErrorException("[{0}]Exception handled processing movie events", scraper.Name, ex);
                    }
                }
            }

            await ProcessQueuedUpdateMeta(queueUpdateMeta).ConfigureAwait(false);
        }


        // 更新
        if (eventType == EventType.Update)
        {
            foreach (var item in movies)
            {
                foreach (var scraper in _scraperManager.All())
                {
                    try
                    {
                        var providerVal = item.GetProviderId(scraper.ProviderId);
                        if (!string.IsNullOrEmpty(providerVal))
                        {
                            var episode = await scraper.GetMediaEpisode(item, providerVal);
                            if (episode != null)
                            {
                                // 下载弹幕xml文件
                                await this.DownloadDanmu(scraper, item, episode.CommentId).ConfigureAwait(false);
                            }

                            // TODO：兼容支持用户设置seasonId？？？
                            break;
                        }
                    }
                    catch (FrequentlyRequestException ex)
                    {
                        _logger.LogErrorException("api接口触发风控，中止执行，请稍候再试.", ex);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogErrorException("Exception handled processing queued movie events", ex);
                    }
                }
            }
        }

        // 强制刷新指定来源弹幕
        if (eventType == EventType.Force)
        {
            _logger.Info("开始处理强制下载任务，共 {0} 个电影", movies.Count);

            // 确保使用已注册弹幕源的 ScraperManager
            EnsureScraperManagerReady();

            // 检查 ScraperManager 是否可用
            if (_scraperManager == null)
            {
                _logger.Error("ScraperManager 为 null，无法处理强制下载任务");
                return;
            }

            var allScrapers = _scraperManager.All();
            _logger.Info("可用的弹幕源数量: {0}", allScrapers.Count);
            if (allScrapers.Count == 0)
            {
                _logger.Warn("没有可用的弹幕源，请检查插件配置");
            }
            
            foreach (var queueItem in movies)
            {
                _logger.Info("处理强制下载任务: {0} (ItemId={1})", queueItem.Name, queueItem.Id);
                _logger.Info("项目 ProviderIds: {0}", string.Join(", ", queueItem.ProviderIds.Select(p => $"{p.Key}={p.Value}")));
                
                // 找到选择的scraper
                var scraper = allScrapers.FirstOrDefault(x => queueItem.ProviderIds.ContainsKey(x.ProviderId));
                if (scraper == null)
                {
                    _logger.Warn("未找到匹配的弹幕源，跳过处理: {0} (ProviderIds={1})", 
                        queueItem.Name, string.Join(", ", queueItem.ProviderIds.Keys));
                    continue;
                }

                _logger.Info("找到弹幕源: {0} (ProviderId={1})", scraper.Name, scraper.ProviderId);

                // 获取选择的弹幕Id
                var mediaId = queueItem.GetProviderId(scraper.ProviderId);
                if (string.IsNullOrEmpty(mediaId))
                {
                    _logger.Warn("未找到弹幕媒体ID，跳过处理: {0}", queueItem.Name);
                    continue;
                }

                _logger.Info("弹幕媒体ID: {0}", mediaId);

                // 获取最新的item数据
                var item = _libraryManager.GetItemById(queueItem.Id);
                if (item == null)
                {
                    _logger.Warn("无法从媒体库获取项目，跳过处理: ItemId={0}", queueItem.Id);
                    continue;
                }

                _logger.Info("获取到媒体项: {0} (Path={1})", item.Name, item.Path ?? "null");

                try
                {
                    var media = await scraper.GetMedia(item, mediaId).ConfigureAwait(false);
                    if (media != null)
                    {
                        _logger.Info("获取到媒体信息: MediaId={0}, CommentId={1}", media.Id, media.CommentId);
                        await this.ForceSaveProviderId(item, scraper.ProviderId, media.Id).ConfigureAwait(false);

                        var episode = await scraper.GetMediaEpisode(item, media.Id).ConfigureAwait(false);
                        if (episode != null)
                        {
                            _logger.Info("开始下载弹幕: {0} (CommentId={1})", item.Name, episode.CommentId);
                            // 下载弹幕xml文件
                            await this.DownloadDanmu(scraper, item, episode.CommentId, true).ConfigureAwait(false);
                            _logger.Info("弹幕下载完成: {0}", item.Name);
                        }
                        else
                        {
                            _logger.Warn("无法获取剧集信息，跳过下载: {0}", item.Name);
                        }
                    }
                    else
                    {
                        _logger.Warn("无法获取媒体信息，跳过下载: {0} (MediaId={1})", item.Name, mediaId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogErrorException("处理强制下载任务时发生错误: {0}", ex, item.Name);
                }
            }
        }
    }


    /// <summary>
    /// Processes queued show events.
    /// </summary>
    /// <param name="events">The <see cref="LibraryEvent"/> enumerable.</param>
    /// <param name="eventType">The <see cref="EventType"/>.</param>
    /// <returns>Task.</returns>
    public async Task ProcessQueuedShowEvents(IReadOnlyCollection<LibraryEvent> events, EventType eventType)
    {
        if (events.Count == 0)
        {
            return;
        }

        _logger.Debug("Processing {Count} shows with event type {EventType}", events.Count, eventType);

        var series = events.Select(lev => (Series)lev.Item)
            .Where(lev => !string.IsNullOrEmpty(lev.Name))
            .ToHashSet();

        try
        {
            if (eventType == EventType.Update)
            {
                foreach (var item in series)
                {
                    var seasons = item.GetSeasons(null, new DtoOptions(false));
                    foreach (var season in seasons)
                    {
                        // 发现season保存元数据，不会推送update事件，这里通过series的update事件推送刷新
                        QueueItem(season, eventType);
                    }
                }
            }

        }
        catch (Exception ex)
        {
            _logger.LogErrorException("Exception handled processing queued show events", ex);
        }
    }

    /// <summary>
    /// Processes queued season events.
    /// </summary>
    /// <param name="events">The <see cref="LibraryEvent"/> enumerable.</param>
    /// <param name="eventType">The <see cref="EventType"/>.</param>
    /// <returns>Task.</returns>
    public async Task ProcessQueuedSeasonEvents(IReadOnlyCollection<LibraryEvent> events, EventType eventType)
    {
        if (events.Count == 0)
        {
            return;
        }

        _logger.Debug("Processing {Count} seasons with event type {EventType}", events.Count, eventType);

        var seasons = events.Select(lev => (Season)lev.Item)
            .Where(lev => !string.IsNullOrEmpty(lev.Name))
            .ToHashSet();


        if (eventType == EventType.Add)
        {
            var queueUpdateMeta = new List<BaseItem>();
            foreach (var season in seasons)
            {
                // // 虚拟季第一次请求忽略
                // if (season.LocationType == LocationType.Virtual && season.IndexNumber is null)
                // {
                //     continue;
                // }

                if (season.IndexNumber.HasValue && season.IndexNumber == 0)
                {
                    _logger.Info("special特典文件夹不处理：name={0} number={1}", season.Name, season.IndexNumber);
                    continue;
                }

                var series = season.GetParent();
                foreach (var scraper in _scraperManager.All())
                {
                    try
                    {
                        // 读取最新数据，要不然取不到年份信息
                        // WARNING：不能对GetItemById的对象直接修改属性，要不然会直接改到数据！！！!
                        // 创建一个临时副本用于搜索，避免直接修改原对象
                        var searchSeason = new Season
                        {
                            Id = season.Id,
                            Name = season.Name,
                            ProductionYear = season.ProductionYear,
                            IndexNumber = season.IndexNumber,
                            ParentIndexNumber = season.ParentIndexNumber,
                            Path = season.Path,
                        };
                        
                        var currentItem = _libraryManager.GetItemById(season.Id);
                        if (currentItem != null)
                        {
                            searchSeason.ProductionYear = currentItem.ProductionYear;
                        }
                        
                        // 季的名称不准确，改使用series的名称
                        if (series != null)
                        {
                            searchSeason.Name = series.Name;
                        }
                        
                        var mediaId = await scraper.SearchMediaId(searchSeason);
                        if (string.IsNullOrEmpty(mediaId))
                        {
                            _logger.Info("[{0}]匹配失败：{1}-{2} ({3})", scraper.Name, series.Name, season.Name, season.ProductionYear);
                            continue;
                        }
                        var media = await scraper.GetMedia(searchSeason, mediaId);
                        if (media == null)
                        {
                            _logger.Info("[{0}]匹配成功，但获取不到视频信息. {1}-{2} id: {3}", scraper.Name, series.Name, season.Name, mediaId);
                            continue;
                        }


                        // 更新seasonId元数据
                        season.SetProviderId(scraper.ProviderId, mediaId);
                        queueUpdateMeta.Add(season);

                        _logger.Info("[{0}]匹配成功：{1}-{2} season_number:{3} ProviderId: {4}", scraper.Name, series.Name, season.Name, season.IndexNumber, mediaId);
                        break;
                    }
                    catch (FrequentlyRequestException ex)
                    {
                        _logger.LogErrorException("api接口触发风控，中止执行，请稍候再试.", ex);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogErrorException("Exception handled processing season events", ex);
                    }
                }
            }

            // 保存元数据
            await ProcessQueuedUpdateMeta(queueUpdateMeta).ConfigureAwait(false);
        }

        if (eventType == EventType.Update)
        {
            foreach (var season in seasons)
            {
                // // 虚拟季第一次请求忽略
                // if (season.LocationType == LocationType.Virtual && season.IndexNumber is null)
                // {
                //     continue;
                // }

                var queueUpdateMeta = new List<BaseItem>();
                // GetEpisodes一定要取所有fields，要不然更新会导致重建虚拟season季信息
                // TODO：可能出现未刮削完，就触发获取弹幕，导致GetEpisodes只能获取到部分剧集的情况
                var episodes = this.GetExistingEpisodes(season);
                if (episodes.Count == 0)
                {
                    continue;
                }

                foreach (var scraper in _scraperManager.All())
                {
                    try
                    {
                        var providerVal = season.GetProviderId(scraper.ProviderId);
                        if (string.IsNullOrEmpty(providerVal))
                        {
                            continue;
                        }

                        var media = await scraper.GetMedia(season, providerVal);
                        if (media == null)
                        {
                            _logger.Info("[{0}]获取不到视频信息. ProviderId: {1}", scraper.Name, providerVal);
                            break;
                        }

                        foreach (var (episode, idx) in episodes.AsEnumerable().Reverse().WithIndex())
                        {
                            var fileName = Path.GetFileName(episode.Path);
                            var indexNumber = episode.IndexNumber ?? 0;
                            if (indexNumber <= 0)
                            {
                                _logger.Info("[{0}]匹配失败，缺少集号. [{1}]{2}", scraper.Name, season.Name, fileName);
                                continue;
                            }

                            if (indexNumber > media.Episodes.Count)
                            {
                                _logger.Info("[{0}]匹配失败，集号超过总集数，可能识别集号错误. [{1}]{2} indexNumber: {3}", scraper.Name, season.Name, fileName, indexNumber);
                                continue;
                            }

                            if (this.Config.DownloadOption.EnableEpisodeCountSame && media.Episodes.Count != episodes.Count)
                            {
                                _logger.Info("[{0}]刷新弹幕失败, 集数不一致。video: {1}.{2} 弹幕数：{3} 集数：{4}", scraper.Name, indexNumber, episode.Name, media.Episodes.Count, episodes.Count);
                                continue;
                            }

                            var epId = media.Episodes[indexNumber - 1].Id;
                            var commentId = media.Episodes[indexNumber - 1].CommentId;
                            _logger.Info("[{0}]成功匹配. {1}.{2} -> epId: {3} cid: {4}", scraper.Name, indexNumber, episode.Name, epId, commentId);

                            // 更新eposide元数据
                            var episodeProviderVal = episode.GetProviderId(scraper.ProviderId);
                            if (!string.IsNullOrEmpty(epId) && episodeProviderVal != epId)
                            {
                                episode.SetProviderId(scraper.ProviderId, epId);
                                queueUpdateMeta.Add(episode);
                            }

                            // 下载弹幕
                            await this.DownloadDanmu(scraper, episode, commentId).ConfigureAwait(false);
                        }

                        break;

                    }
                    catch (FrequentlyRequestException ex)
                    {
                        _logger.LogErrorException("api接口触发风控，中止执行，请稍候再试.", ex);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogErrorException("Exception handled processing queued movie events", ex);
                    }
                }

                // 保存元数据
                await ProcessQueuedUpdateMeta(queueUpdateMeta).ConfigureAwait(false);
            }
        }
    }



    /// <summary>
    /// Processes queued episode events.
    /// </summary>
    /// <param name="events">The <see cref="LibraryEvent"/> enumerable.</param>
    /// <param name="eventType">The <see cref="EventType"/>.</param>
    /// <returns>Task.</returns>
    public async Task ProcessQueuedEpisodeEvents(IReadOnlyCollection<LibraryEvent> events, EventType eventType)
    {
        if (events.Count == 0)
        {
            return;
        }

        _logger.Debug("Processing {Count} episodes with event type {EventType}", events.Count, eventType);

        var items = events.Select(lev => (Episode)lev.Item)
            .Where(lev => !string.IsNullOrEmpty(lev.Name))
            .ToHashSet();


        // 判断epid，有的话刷新弹幕文件
        if (eventType == EventType.Update)
        {
            var queueUpdateMeta = new List<BaseItem>();
            foreach (var item in items)
            {
                // 如果 Episode 没有弹幕元数据，表示该集是刮削完成后再新增的，需要重新匹配获取
                var scrapers = this._scraperManager.All();
                var season = item.Season;
                var allDanmuProviderIds = scrapers.Select(x => x.ProviderId).ToList();
                var episodeFirstProviderId = allDanmuProviderIds.FirstOrDefault(x => !string.IsNullOrEmpty(item.GetProviderId(x)));
                var seasonFirstProviderId = allDanmuProviderIds.FirstOrDefault(x => !string.IsNullOrEmpty(season.GetProviderId(x)));
                if (string.IsNullOrEmpty(episodeFirstProviderId) && !string.IsNullOrEmpty(seasonFirstProviderId) && item.IndexNumber.HasValue)
                {
                    var scraper = scrapers.First(x => x.ProviderId == seasonFirstProviderId);
                    var providerVal = season.GetProviderId(seasonFirstProviderId);

                    if (scraper == null)
                    {
                        _logger.Info("找不到对应的弹幕来源. ProviderId: {0}", seasonFirstProviderId);
                        continue;
                    }

                    var media = await scraper.GetMedia(season, providerVal);
                    if (media != null)
                    {
                        var fileName = Path.GetFileName(item.Path);
                        var indexNumber = item.IndexNumber ?? 0;
                        if (indexNumber <= 0)
                        {
                            this._logger.Info("[{0}]匹配失败，缺少集号. [{1}]{2}", scraper.Name, season.Name, fileName);
                            continue;
                        }

                        if (indexNumber > media.Episodes.Count)
                        {
                            this._logger.Info("[{0}]匹配失败，集号超过总集数，可能识别集号错误. [{1}]{2} indexNumber: {3}", scraper.Name, season.Name, fileName, indexNumber);
                            continue;
                        }

                        var episodes = this.GetExistingEpisodes(season);
                        if (this.Config.DownloadOption.EnableEpisodeCountSame && media.Episodes.Count != episodes.Count)
                        {
                            this._logger.Info("[{0}]刷新弹幕失败, 集数不一致。video: {1}.{2} 弹幕数：{3} 集数：{4}", scraper.Name, indexNumber, item.Name, media.Episodes.Count, episodes.Count);
                            continue;
                        }

                        var idx = indexNumber - 1;
                        var epId = media.Episodes[idx].Id;
                        var commentId = media.Episodes[idx].CommentId;
                        this._logger.Info("[{0}]成功匹配. {1}.{2} -> epId: {3} cid: {4}", scraper.Name, item.IndexNumber, item.Name, epId, commentId);

                        // 更新 eposide 元数据
                        var episodeProviderVal = item.GetProviderId(scraper.ProviderId);
                        if (!string.IsNullOrEmpty(epId) && episodeProviderVal != epId)
                        {
                            item.SetProviderId(scraper.ProviderId, epId);
                            queueUpdateMeta.Add(item);
                        }

                        // 下载弹幕
                        await this.DownloadDanmu(scraper, item, commentId).ConfigureAwait(false);
                        continue;
                    }
                }


                // 刷新弹幕
                foreach (var scraper in _scraperManager.All())
                {
                    try
                    {
                        var providerVal = item.GetProviderId(scraper.ProviderId);
                        if (string.IsNullOrEmpty(providerVal))
                        {
                            continue;
                        }

                        var episode = await scraper.GetMediaEpisode(item, providerVal);
                        if (episode != null)
                        {
                            // 下载弹幕xml文件
                            await this.DownloadDanmu(scraper, item, episode.CommentId).ConfigureAwait(false);
                        }
                        break;
                    }
                    catch (FrequentlyRequestException ex)
                    {
                        _logger.LogErrorException("api接口触发风控，中止执行，请稍候再试.", ex);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogErrorException("Exception handled processing queued movie events", ex);
                    }
                }
            }

            // 保存元数据
            await ProcessQueuedUpdateMeta(queueUpdateMeta).ConfigureAwait(false);
        }


        // 强制刷新指定来源弹幕（手动搜索强刷忽略集数不一致处理）
        if (eventType == EventType.Force)
        {
            _logger.Info("开始处理强制下载任务，共 {0} 个剧集", items.Count);

            // 确保使用已注册弹幕源的 ScraperManager
            EnsureScraperManagerReady();

            // 检查 ScraperManager 是否可用
            if (_scraperManager == null)
            {
                _logger.Error("ScraperManager 为 null，无法处理强制下载任务");
                return;
            }

            var allScrapers = _scraperManager.All();
            _logger.Info("可用的弹幕源数量: {0}", allScrapers.Count);
            if (allScrapers.Count == 0)
            {
                _logger.Warn("没有可用的弹幕源，请检查插件配置");
            }
            
            foreach (var queueItem in items)
            {
                _logger.Info("处理强制下载任务: {0} (ItemId={1})", queueItem.Name, queueItem.Id);
                _logger.Info("项目 ProviderIds: {0}", string.Join(", ", queueItem.ProviderIds.Select(p => $"{p.Key}={p.Value}")));
                
                // 找到选择的scraper
                var scraper = allScrapers.FirstOrDefault(x => queueItem.ProviderIds.ContainsKey(x.ProviderId));
                if (scraper == null)
                {
                    _logger.Warn("未找到匹配的弹幕源，跳过处理: {0} (ProviderIds={1})", 
                        queueItem.Name, string.Join(", ", queueItem.ProviderIds.Keys));
                    continue;
                }

                _logger.Info("找到弹幕源: {0} (ProviderId={1})", scraper.Name, scraper.ProviderId);

                // 获取选择的弹幕Id
                var mediaId = queueItem.GetProviderId(scraper.ProviderId);
                if (string.IsNullOrEmpty(mediaId))
                {
                    _logger.Warn("未找到弹幕媒体ID，跳过处理: {0}", queueItem.Name);
                    continue;
                }

                _logger.Info("弹幕媒体ID: {0}", mediaId);

                // 获取最新的item数据
                var item = _libraryManager.GetItemById(queueItem.Id);
                if (item == null)
                {
                    _logger.Warn("无法从媒体库获取项目，跳过处理: ItemId={0}", queueItem.Id);
                    continue;
                }

                var season = ((Episode)item).Season;
                if (season == null)
                {
                    _logger.Warn("无法获取季信息，跳过处理: {0}", item.Name);
                    continue;
                }

                _logger.Info("获取到媒体项: {0} (Path={1}, Season={2})", item.Name, item.Path ?? "null", season.Name);

                try
                {
                    var media = await scraper.GetMedia(season, mediaId).ConfigureAwait(false);
                    if (media != null)
                    {
                        _logger.Info("获取到媒体信息: MediaId={0}, EpisodeCount={1}", media.Id, media.Episodes?.Count ?? 0);
                        // 更新季元数据
                        await ForceSaveProviderId(season, scraper.ProviderId, media.Id).ConfigureAwait(false);

                        // 更新所有剧集元数据，GetEpisodes一定要取所有fields，要不然更新会导致重建虚拟season季信息
                        var episodeList = season.GetEpisodes().Items;
                        _logger.Info("开始处理剧集列表，共 {0} 集", episodeList.Count());
                        foreach (var (episode, idx) in episodeList.Reverse().WithIndex())
                        {
                            var fileName = Path.GetFileName(episode.Path);

                        // 没对应剧集号的，忽略处理
                        var indexNumber = episode.IndexNumber ?? 0;
                        if (indexNumber < 1)
                        {
                            _logger.Info("[{0}]缺少集号，忽略处理. [{1}]{2}", scraper.Name, season.Name, fileName);
                            continue;
                        }

                        if (indexNumber > media.Episodes.Count)
                        {
                            _logger.Info("[{0}]集号超过弹幕数，忽略处理. [{1}]{2} 集号: {3} 弹幕数：{4}", scraper.Name, season.Name, fileName, indexNumber, media.Episodes.Count);
                            continue;
                        }

                        // 特典或extras影片不处理（动画经常会放在季文件夹下）
                        if (episode.ParentIndexNumber is null or 0)
                        {
                            _logger.Info("[{0}]缺少季号，可能是特典或extras影片，忽略处理. [{1}]{2}", scraper.Name, season.Name, fileName);
                            continue;
                        }

                        var epId = media.Episodes[indexNumber - 1].Id;
                        var commentId = media.Episodes[indexNumber - 1].CommentId;

                        _logger.Info("开始下载弹幕: {0}.{1} (CommentId={2})", episode.IndexNumber, episode.Name, commentId);
                        // 下载弹幕xml文件
                        await this.DownloadDanmu(scraper, episode, commentId, true).ConfigureAwait(false);
                        _logger.Info("弹幕下载完成: {0}.{1}", episode.IndexNumber, episode.Name);

                        // 更新剧集元数据
                        await ForceSaveProviderId(episode, scraper.ProviderId, epId);
                    }
                    }
                    else
                    {
                        _logger.Warn("无法获取媒体信息，跳过下载: {0} (MediaId={1})", season.Name, mediaId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogErrorException("处理强制下载任务时发生错误: {0}", ex, item.Name);
                }
            }
        }
    }

    private List<BaseItem> GetExistingEpisodes(Season season)
    {
        var episodes = season.GetEpisodes().Items
            .Where(i => !i.IsVirtualItem)
            .ToList();
        // 不处理季文件夹下的特典和extras影片（动画经常会混在一起）
        var episodesWithoutSP = episodes
            .Where(x => x.ParentIndexNumber != null && x.ParentIndexNumber > 0)
            .ToList();
        if (episodes.Count != episodesWithoutSP.Count)
        {
            _logger.Info("{0}季存在{1}个特典或extra片段，忽略处理.", season.Name, (episodes.Count - episodesWithoutSP.Count));
            episodes = episodesWithoutSP;
        }
        return episodes;
    }

    // 调用UpdateToRepositoryAsync后，但未完成时，会导致GetEpisodes返回缺少正在处理的集数，所以采用统一最后处理
    private async Task ProcessQueuedUpdateMeta(List<BaseItem> queue)
    {
        if (queue == null || queue.Count <= 0)
        {
            return;
        }

        foreach (var queueItem in queue)
        {
            // 获取最新的item数据
            var item = _libraryManager.GetItemById(queueItem.Id);
            if (item != null)
            {
                // 合并新添加的provider id
                foreach (var pair in queueItem.ProviderIds)
                {
                    if (string.IsNullOrEmpty(pair.Value))
                    {
                        continue;
                    }

                    item.ProviderIds[pair.Key] = pair.Value;
                }

                item.UpdateToRepository(ItemUpdateType.MetadataEdit);
            }
        }
        _logger.Info("更新epid到元数据完成。item数：{0}", queue.Count);
    }

    public async Task DownloadDanmu(AbstractScraper scraper, BaseItem item, string commentId, bool ignoreCheck = false)
    {
        // 下载弹幕xml文件
        var checkDownloadedKey = $"{item.Id}_{commentId}";
        _logger.Info("[{0}]DownloadDanmu 方法开始: name={1}.{2}, commentId={3}, ignoreCheck={4}", 
            scraper.Name, item.IndexNumber ?? 1, item.Name, commentId, ignoreCheck);
        
        try
        {
            // 弹幕5分钟内更新过，忽略处理（有时Update事件会重复执行）
            if (!ignoreCheck && _memoryCache.TryGetValue(checkDownloadedKey, out var latestDownloaded))
            {
                _logger.Info("[{0}]最近5分钟已更新过弹幕xml，忽略处理：{1}.{2}", scraper.Name, item.IndexNumber, item.Name);
                return;
            }

            _logger.Info("[{0}]开始获取弹幕内容: name={1}.{2}, commentId={3}", scraper.Name, item.IndexNumber ?? 1, item.Name);
            _memoryCache.Set(checkDownloadedKey, true, _danmuUpdatedExpiredOption);
            var danmaku = await scraper.GetDanmuContent(item, commentId).ConfigureAwait(false);
            _logger.Info("[{0}]获取弹幕内容完成: name={1}.{2}, danmaku={3}", 
                scraper.Name, item.IndexNumber ?? 1, item.Name, danmaku != null ? "非空" : "null");
            if (danmaku != null)
            {
                if (danmaku.Items.Count <= 0)
                {
                    _logger.Info("[{0}]弹幕内容为空，忽略处理：{1}.{2}", scraper.Name, item.IndexNumber, item.Name);
                    return;
                }

                // 为了避免bilibili下架视频后，返回的失效弹幕内容把旧弹幕覆盖掉，这里做个内容判断
                var bytes = danmaku.ToXml();
                if (bytes.Length < 1024 && scraper.ProviderName == Emby.Plugin.Danmu.Scrapers.Bilibili.Bilibili.ScraperProviderName)
                {
                    _logger.Info("[{0}]弹幕内容少于1KB，可能是已失效弹幕，忽略处理：{1}.{2}", scraper.Name, item.IndexNumber, item.Name);
                    return;
                }
                _logger.Info("[{0}]开始保存弹幕文件: name={1}.{2}, bytes长度={3}", 
                    scraper.Name, item.IndexNumber ?? 1, item.Name, bytes.Length);
                var savedPaths = await this.SaveDanmu(item, bytes).ConfigureAwait(false);
                this._logger.Info("[{0}]弹幕下载成功：name={1}.{2} commentId={3}", scraper.Name, item.IndexNumber ?? 1, item.Name, commentId);
                if (!string.IsNullOrEmpty(savedPaths))
                {
                    this._logger.Info("[{0}]弹幕文件保存路径：{1}", scraper.Name, savedPaths);
                }
                else
                {
                    this._logger.Warn("[{0}]弹幕文件保存路径为空", scraper.Name);
                }
            }
            else
            {
                _memoryCache.Remove(checkDownloadedKey);
            }
        }
        catch (Exception ex)
        {
            _memoryCache.Remove(checkDownloadedKey);
            _logger.LogErrorException("[{0}]DownloadDanmu 方法发生异常: name={1}, commentId={2}", 
                scraper.Name, item.Name, commentId, ex);
            throw; // 重新抛出异常，让调用者知道发生了错误
        }
    }

    private bool IsRepeatAction(BaseItem item, string checkDownloadedKey)
    {
        // 单元测试时为null
        if (item.FileNameWithoutExtension == null) return false;

        // 通过xml文件属性判断（多线程时判断有误）
        var danmuPath = Path.Combine(item.ContainingFolderPath, item.FileNameWithoutExtension + ".xml");
        if (!this._fileSystem.Exists(danmuPath))
        {
            return false;
        }

        var lastWriteTime = this._fileSystem.GetLastWriteTime(danmuPath);
        var diff = DateTime.Now - lastWriteTime;
        return diff.TotalSeconds < 300;
    }

    private async Task<string> SaveDanmu(BaseItem item, byte[] bytes)
    {
        // 单元测试时为null
        if (item.FileNameWithoutExtension == null) return string.Empty;

        var savedPaths = new List<string>();

        // 下载弹幕xml文件
        var danmuPath = Path.Combine(item.ContainingFolderPath, item.FileNameWithoutExtension + ".xml");
        await this._fileSystem.WriteAllBytesAsync(danmuPath, bytes, CancellationToken.None).ConfigureAwait(false);
        savedPaths.Add(danmuPath);

        if (this.Config.ToAss && bytes.Length > 0)
        {
            var assConfig = new Danmaku2Ass.Config();
            assConfig.Title = item.Name;
            if (!string.IsNullOrEmpty(this.Config.AssFont.Trim()))
            {
                assConfig.FontName = this.Config.AssFont;
            }
            if (!string.IsNullOrEmpty(this.Config.AssFontSize.Trim()))
            {
                assConfig.BaseFontSize = this.Config.AssFontSize.Trim().ToInt();
            }
            if (!string.IsNullOrEmpty(this.Config.AssTextOpacity.Trim()))
            {
                assConfig.TextOpacity = this.Config.AssTextOpacity.Trim().ToFloat();
            }
            if (!string.IsNullOrEmpty(this.Config.AssLineCount.Trim()))
            {
                assConfig.LineCount = this.Config.AssLineCount.Trim().ToInt();
            }
            if (!string.IsNullOrEmpty(this.Config.AssSpeed.Trim()))
            {
                assConfig.TuneDuration = this.Config.AssSpeed.Trim().ToInt() - 8;
            }
            if (this.Config.AssRemoveEmoji)
            {
                Danmaku2Ass.Bilibili.GetInstance().SetCustomFilter(true);
            }

            var assPath = Path.Combine(item.ContainingFolderPath, item.FileNameWithoutExtension + ".danmu.ass");
            Danmaku2Ass.Bilibili.GetInstance().Create(bytes, assConfig, assPath);
            savedPaths.Add(assPath);
        }

        return string.Join(", ", savedPaths);
    }

    private async Task ForceSaveProviderId(BaseItem item, string providerId, string providerVal)
    {
        // 先清空旧弹幕的所有元数据
        foreach (var s in _scraperManager.All())
        {
            item.ProviderIds.Remove(s.ProviderId);
        }
        // 保存指定弹幕元数据
        item.ProviderIds[providerId] = providerVal;

        item.UpdateToRepository(ItemUpdateType.MetadataEdit);
    }


    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _queueTimer?.Dispose();
        }
    }
}
