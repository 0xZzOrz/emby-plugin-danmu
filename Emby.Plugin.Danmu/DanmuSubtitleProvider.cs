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
            ScraperManager scraperManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(GetType().Name);
            _scraperManager = scraperManager;
            _libraryManagerEventsHelper = Core.SingletonManager.LibraryManagerEventsHelper;
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
        }

        public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
        {
            _logger.Info("开始查询弹幕 id={0}", id);
            if (_memoryCache.TryGetValue(id, out bool has))
            {
                if (has)
                {
                    throw new CanIgnoreException($"已经触发下载了，无需重试");
                }
            }
            
            var base64EncodedBytes = System.Convert.FromBase64String(id);
            id = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            var info = id.FromJson<SubtitleId>();
            if (info == null)
            {
                throw new ArgumentException();
            }

            var item = _libraryManager.GetItemById(info.ItemId);
            if (item == null)
            {
                throw new ArgumentException();
            }

            var scraper = _scraperManager.All().FirstOrDefault(x => x.ProviderId == info.ProviderId);
            if (scraper != null)
            {
                // 创建一个临时的 BaseItem 来传递 ProviderId，避免直接修改原始库项目
                var tempItem = CreateTemporaryItemWithProviderId(item, scraper.ProviderId, info.Id);
                if (tempItem != null)
                {
                    _libraryManagerEventsHelper.QueueItem(tempItem, EventType.Force);
                }
            }

            _memoryCache.Set<bool>(id, true, _pendingDanmuDownloadExpiredOption);
            throw new CanIgnoreException($"'{item.Name}' 的弹幕任务已在后台开始下载，请稍后查看");
        }

        public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request, CancellationToken cancellationToken)
        {
            // 防御：核心依赖未就绪时直接返回
            if (_scraperManager == null)
            {
                _logger.Warn("ScraperManager is null, skip danmu search.");
                return Array.Empty<RemoteSubtitleInfo>();
            }

            // LibraryManagerEventsHelper 可能尚未在构造阶段注入完成
            if (_libraryManagerEventsHelper == null)
            {
                _logger.Warn("LibraryManagerEventsHelper is null, skip danmu search.");
                return Array.Empty<RemoteSubtitleInfo>();
            }

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
            var searchTasks = _scraperManager.All()?.Select(async scraper =>
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

