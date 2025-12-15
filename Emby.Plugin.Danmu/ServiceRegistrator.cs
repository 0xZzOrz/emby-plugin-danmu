using System;
using Emby.Plugin.Danmu.Core;
using Emby.Plugin.Danmu.Core.Extensions;
using Emby.Plugin.Danmu.Scrapers;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugin.Danmu
{
    /// <summary>
    /// Service registrator for Emby plugin.
    /// </summary>
    public class ServiceRegistrator : IServerEntryPoint
    {
        private readonly ILogManager _logManager;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IApplicationHost _applicationHost;
        private PluginStartup _pluginStartup;

        public ServiceRegistrator(
            ILogManager logManager,
            IJsonSerializer jsonSerializer,
            IApplicationHost applicationHost,
            ILibraryManager libraryManager,
            ScraperManager scraperManager)
        {
            _logManager = logManager;
            _jsonSerializer = jsonSerializer;
            _applicationHost = applicationHost;
            _logger = logManager.GetLogger(GetType().Name);
            
            // Initialize JsonExtension
            JsonExtension.Initialize(jsonSerializer);
            
            // Initialize SingletonManager
            Core.SingletonManager.ScraperManager = scraperManager;
            Core.SingletonManager.JsonSerializer = jsonSerializer;
            Core.SingletonManager.LogManager = logManager;
            Core.SingletonManager.applicationHost = applicationHost;
            
            // Create LibraryManagerEventsHelper instance
            var libraryManagerEventsHelper = new LibraryManagerEventsHelper(libraryManager, logManager, scraperManager);
            Core.SingletonManager.LibraryManagerEventsHelper = libraryManagerEventsHelper;
            
            // Create and start PluginStartup
            _pluginStartup = new PluginStartup(libraryManager, logManager, libraryManagerEventsHelper);
        }

        public void Run()
        {
            _logger.Info("Danmu 插件服务注册完成");
            _pluginStartup.Start();
        }

        public void Dispose()
        {
            _pluginStartup?.Dispose();
        }
    }
}
