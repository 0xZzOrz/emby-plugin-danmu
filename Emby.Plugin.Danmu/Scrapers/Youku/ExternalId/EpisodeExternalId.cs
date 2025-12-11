using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.Danmu.Scrapers.Youku.ExternalId
{
    /// <inheritdoc />
    public class EpisodeExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => Youku.ScraperProviderName;

        /// <inheritdoc />
        public string Key => Youku.ScraperProviderId;
        /// <inheritdoc />
        public string Name => ProviderName;

        /// <inheritdoc />
        public string? UrlFormatString => null;

        /// <inheritdoc />
        
        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Episode;
    }
}
