using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Emby.Plugin.Danmu.Configuration;
using Emby.Plugin.Danmu.Scrapers;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugin.Danmu
{
    /// <summary>
    /// The main plugin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasThumbImage, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="applicationHost">Instance of the <see cref="IApplicationHost"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
        /// <param name="scraperManager">Instance of the <see cref="ScraperManager"/> class.</param>
        public Plugin(
            IApplicationPaths applicationPaths,
            IApplicationHost applicationHost,
            IXmlSerializer xmlSerializer,
            ILogManager logManager,
            ScraperManager scraperManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            var logger = logManager.GetLogger(GetType().Name);

            Scrapers = applicationHost.GetExports<AbstractScraper>(false)
                .Where(o => o != null && !o.IsDeprecated)
                .OrderBy(x => x.DefaultOrder)
                .ToList()
                .AsReadOnly();

            scraperManager.Register(Scrapers);
            logger.Info("Danmu 插件加载完成, 支持 {0} 个弹幕源", Scrapers.Count);

            // 详细注册信息改为 Debug，减少日志量
            foreach (var scraper in Scrapers)
            {
                logger.Debug("注册弹幕源: Name={0}, ProviderId={1}, DefaultEnable={2}",
                    scraper.Name, scraper.ProviderId, scraper.DefaultEnable);
            }

            // 将注册了 scrapers 的 ScraperManager 实例保存到 SingletonManager
            Core.SingletonManager.ScraperManager = scraperManager;
            logger.Debug("Danmu 插件: ScraperManager 已保存到 SingletonManager (内部注册数量={0})",
                scraperManager.AllWithNoEnabled().Count);
        }

        /// <inheritdoc />
        public override string Name => "Danmu";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("5B39DA44-5314-4940-8E26-54C821C17F86");

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; }

        /// <summary>
        /// 全部的弹幕源
        /// </summary>
        public ReadOnlyCollection<AbstractScraper> Scrapers { get; }

        /// <summary>
        /// Gets the thumb image format.
        /// </summary>
        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        /// <summary>
        /// Gets the thumb image.
        /// </summary>
        /// <returns>An image stream.</returns>
        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".Configuration.logo.png");
        }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    DisplayName = "弹幕配置",
                    MenuIcon = "closed_caption",
                    MenuSection = "server",
                    EnableInMainMenu = true,
                    EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
                },
                new PluginPageInfo
                {
                    Name = "danmuJs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.js"
                }
            };
        }
    }
}

