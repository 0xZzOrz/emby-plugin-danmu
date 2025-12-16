using System;
using System.Threading.Tasks;
using Emby.Plugin.Danmu.Scrapers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Emby.Plugin.Danmu.Core.Extensions
{
    public static class BaseItemExtension
    {
        public static Task UpdateToRepositoryAsync(this BaseItem item, ItemUpdateType itemUpdateType, System.Threading.CancellationToken cancellationToken)
        {
            item.UpdateToRepository(ItemUpdateType.MetadataEdit);
            return Task.CompletedTask;
        }

        public static string GetDanmuXmlPath(this BaseItem item, string providerId)
        {
            return item.FileNameWithoutExtension + "_" + providerId + ".xml";
        }

        /// <summary>
        /// 获取弹幕id
        /// </summary>
        public static string GetDanmuProviderId(this BaseItem item, string providerId)
        {
            string providerVal = item.GetProviderId(providerId);
            if (!string.IsNullOrEmpty(providerVal))
            {
                return providerVal;
            }

            if (item is Season season)
            {
                return season.GetParent()?.GetProviderId(providerId) ?? providerVal;
            }
            return providerVal;
        }

        /// <summary>
        /// season 获取id问题，可能存在没有season的问题，需要使用SeriesId
        /// </summary>
        public static Guid GetSeasonId(this Season season)
        {
            Guid seasonId = season.Id;
            if (!Guid.Empty.Equals(seasonId))
            {
                return seasonId;
            }

            return season.GetParent()?.Id ?? Guid.Empty;
        }

        /// <summary>
        /// 是否存在相应的id
        /// </summary>
        public static bool HasAnyDanmuProviderIds(this BaseItem item, ScraperManager scraperManager)
        {
            var scrapers = scraperManager.All();
            if (scrapers == null || scrapers.Count == 0)
            {
                return false;
            }

            foreach (var scraper in scrapers)
            {
                if (item.HasProviderId(scraper.ProviderId))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

