using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.Danmu.Core;
using Emby.Plugin.Danmu.Core.Extensions;
using Emby.Plugin.Danmu.Model;
using Emby.Plugin.Danmu.Scrapers;
using Emby.Plugin.Danmu.Scrapers.Entity;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Caching.Memory;

namespace Emby.Plugin.Danmu
{
    public class DanmuSubtitleProvider : ISubtitleProvider
    {
        public string Name => "Danmu";

        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly LibraryManagerEventsHelper _libraryManagerEventsHelper;
        private readonly ScraperManager _scraperManager;
        private readonly IMemoryCache _memoryCache;
        
        private readonly MemoryCacheEntryOptions _pendingDanmuDownloadExpiredOption = new MemoryCacheEntryOptions()
            { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) };

        public IEnumerable<VideoContentType> SupportedMediaTypes => new List<VideoContentType>()
        {
            VideoContentType.Movie, 
            VideoContentType.Episode, 
        };

        public DanmuSubtitleProvider(
            ILibraryManager libraryManager,
            ILogManager logManager,
            ScraperManager scraperManager,
            LibraryManagerEventsHelper libraryManagerEventsHelper = null)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(GetType().Name);
            // 使用 SingletonManager 中的 ScraperManager，确保使用的是注册了 scrapers 的实例
            _scraperManager = Core.SingletonManager.ScraperManager ?? scraperManager;
            _libraryManagerEventsHelper = libraryManagerEventsHelper ?? Core.SingletonManager.LibraryManagerEventsHelper;
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            
            _logger.Info("DanmuSubtitleProvider 初始化: ScraperManager={0}, LibraryManagerEventsHelper={1}", 
                _scraperManager != null ? "非空" : "null",
                _libraryManagerEventsHelper != null ? "非空" : "null");
            
            if (_scraperManager != null)
            {
                var allScrapers = _scraperManager.AllWithNoEnabled();
                _logger.Info("DanmuSubtitleProvider 初始化: 内部注册的 scrapers 数量={0}", allScrapers?.Count ?? 0);
            }
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            _logger.Info("开始查询弹幕 GetSubtitles id={0}", id);
            if (_memoryCache.TryGetValue(id, out bool has))
            {
                if (has)
                {
                    _logger.Info("弹幕下载任务已存在，跳过重复请求");
                    throw new CanIgnoreException($"已经触发下载了，无需重试");
                }
            }
            
            var base64EncodedBytes = System.Convert.FromBase64String(id);
            id = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            var info = id.FromJson<SubtitleId>();
            if (info == null)
            {
                _logger.Error("无法解析弹幕ID信息");
                throw new ArgumentException();
            }

            var item = _libraryManager.GetItemById(info.ItemId);
            if (item == null)
            {
                _logger.Error("未找到媒体项 ItemId={0}", info.ItemId);
                throw new ArgumentException();
            }

            var scraper = _scraperManager.All().FirstOrDefault(x => x.ProviderId == info.ProviderId);
            if (scraper != null)
            {
                _logger.Info("找到弹幕源 {0}，开始下载弹幕 ItemId={1}, MediaId={2}", scraper.Name, info.ItemId, info.Id);
                // 创建一个临时的 BaseItem 来传递 ProviderId，避免直接修改原始库项目
                var tempItem = CreateTemporaryItemWithProviderId(item, scraper.ProviderId, info.Id);
                if (tempItem != null)
                {
                    _libraryManagerEventsHelper.QueueItem(tempItem, EventType.Force);
                    _logger.Info("弹幕下载任务已加入队列: {0}", item.Name);
                }
                else
                {
                    _logger.Warn("无法创建临时媒体项，跳过弹幕下载");
                }
            }
            else
            {
                _logger.Warn("未找到弹幕源 ProviderId={0}", info.ProviderId);
            }

            _memoryCache.Set<bool>(id, true, _pendingDanmuDownloadExpiredOption);
            // 这是预期的异常，用于告知 Emby 任务已在后台开始下载
            // 注意：Emby 会将此异常记录为错误，但这是正常的工作流程，不影响功能
            _logger.Info("弹幕下载任务已启动，返回提示信息（后续的错误日志是预期的，可忽略）");
            throw new CanIgnoreException($"'{item.Name}' 的弹幕任务已在后台开始下载，请稍后查看");
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            if (request.Language == "zh-CN" || request.Language == "zh-TW" || request.Language == "zh-HK")
            {
                request.Language = "chi";
            }
            if (request.Language != "chi")
            {
                return Array.Empty<RemoteSubtitleInfo>();
            }
            
            var list = new List<RemoteSubtitleInfo>();
            if (string.IsNullOrEmpty(request.MediaPath))
            {
                return list;
            }

            var item = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Path = request.MediaPath,
            }).FirstOrDefault();

            if (item == null)
            {
                return list;
            }

            // 媒体库未启用就不处理
            if (_libraryManagerEventsHelper != null && _libraryManagerEventsHelper.IsIgnoreItem(item))
            {
                return list;
            }

            // 剧集使用series名称进行搜索
            if (item is Episode episode && !string.IsNullOrWhiteSpace(request.SeriesName))
            {
                item.Name = request.SeriesName;
            }

            // 并行执行所有scraper的搜索
            var allScrapers = _scraperManager.All();
            if (allScrapers == null || allScrapers.Count == 0)
            {
                _logger.Warn("弹幕搜索: 没有可用的弹幕源");
                return list;
            }

            var searchTasks = allScrapers.Select(async scraper =>
            {
                try
                {
                    if (scraper == null)
                    {
                        return new List<RemoteSubtitleInfo>();
                    }

                    var result = await scraper.Search(item).ConfigureAwait(false) ?? new List<ScraperSearchInfo>();
                    var subtitles = new List<RemoteSubtitleInfo>();
                    
                    foreach (var searchInfo in result)
                    {
                        var title = searchInfo.Name;
                        if (!string.IsNullOrEmpty(searchInfo.Category))
                        {
                            title = $"[{searchInfo.Category}] {searchInfo.Name}";
                        }
                        if (searchInfo.Year != null && searchInfo.Year > 0 && searchInfo.Year > 1970)
                        {
                            title += $" ({searchInfo.Year})";
                        }
                        if (item is Episode && searchInfo.EpisodeSize > 0)
                        {
                            title += $"【共{searchInfo.EpisodeSize}集】";
                        }
                        var idInfo = new SubtitleId() { ItemId = item.Id.ToString(), Id = searchInfo.Id.ToString(), ProviderId = scraper.ProviderId };
                        subtitles.Add(new RemoteSubtitleInfo()
                        {
                            Id = idInfo.ToJson().ToBase64(),  // Id不允许特殊字幕，做base64编码处理
                            Name = $"{title} - 来源：{scraper.Name} 弹幕",
                            ProviderName = $"{Name}",
                            Language = "zh-CN",
                            Format = "ass",
                            Comment = $"来源：{scraper.Name} 弹幕",
                        });
                    }
                    
                    return subtitles;
                }
                catch (Exception ex)
                {
                    _logger.LogErrorException("[{0}]Exception handled processing queued movie events", ex, scraper.Name);
                    return new List<RemoteSubtitleInfo>();
                }
            });

            if (searchTasks == null)
            {
                return list;
            }

            var results = await Task.WhenAll(searchTasks).ConfigureAwait(false);
            
            // 合并所有结果
            foreach (var subtitles in results)
            {
                list.AddRange(subtitles);
            }

            return list;
        }

        /// <summary>
        /// 创建一个临时的 BaseItem，用于向事件队列传递 ProviderId。
        /// </summary>
        /// <param name="originalItem">原始的媒体项目。</param>
        /// <param name="providerId">弹幕源的 ProviderId。</param>
        /// <param name="mediaId">弹幕源的媒体ID。</param>
        /// <returns>一个包含新 ProviderId 的临时 BaseItem。</returns>
        private BaseItem CreateTemporaryItemWithProviderId(BaseItem originalItem, string providerId, string mediaId)
        {
            BaseItem tempItem = null;
            var providerIds = new ProviderIdDictionary { { providerId, mediaId } };

            if (originalItem is Movie)
            {
                tempItem = new Movie { Id = originalItem.Id, Name = originalItem.Name, ProviderIds = providerIds };
            }
            else if (originalItem is Episode)
            {
                tempItem = new Episode { Id = originalItem.Id, Name = originalItem.Name, ProviderIds = providerIds };
            }

            return tempItem;
        }
    }
}

