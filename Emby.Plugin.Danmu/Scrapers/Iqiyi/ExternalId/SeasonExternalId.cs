using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.Danmu.Scrapers.Iqiyi.ExternalId
{

    /// <inheritdoc />
    public class SeasonExternalId : IExternalId
    {
        /// <inheritdoc />
        public string Name => Iqiyi.ScraperProviderName;

        /// <inheritdoc />
        public string Key => Iqiyi.ScraperProviderId;
        /// <inheritdoc />
        public string UrlFormatString => "https://example.com/season/{0}";

        /// <inheritdoc />

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item) => item is Season;
    }

}
