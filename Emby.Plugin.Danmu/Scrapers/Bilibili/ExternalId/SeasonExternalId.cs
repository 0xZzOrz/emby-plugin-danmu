using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Emby.Plugin.Danmu.Scrapers.Bilibili.ExternalId
{
    /// <inheritdoc />
    public class SeasonExternalId : IExternalId
    {
        /// <inheritdoc />
        public string Name => Bilibili.ScraperProviderName;

        /// <inheritdoc />
        public string Key => Bilibili.ScraperProviderId;

        /// <inheritdoc />
        public string UrlFormatString => "https://www.bilibili.com/bangumi/play/ss{0}";

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Season;
    }
}
