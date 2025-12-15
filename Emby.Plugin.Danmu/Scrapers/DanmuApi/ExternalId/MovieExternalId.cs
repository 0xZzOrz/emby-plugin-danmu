using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.Danmu.Scrapers.DanmuApi.ExternalId
{
    /// <inheritdoc />
    public class MovieExternalId : IExternalId
    {
        /// <inheritdoc />
        public string Name => DanmuApi.ScraperProviderName;

        /// <inheritdoc />
        public string Key => DanmuApi.ScraperProviderId;
        /// <inheritdoc />
        public string UrlFormatString => "https://example.com/movie/{0}";

        /// <inheritdoc />

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Movie;
    }
}
