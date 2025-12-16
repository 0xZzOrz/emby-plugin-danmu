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
            
            // 使用 Plugin.Instance 中已经注册了 scrapers 的 ScraperManager
            // 因为 Plugin.cs 中已经注册了所有 scrapers 并保存到 SingletonManager
            var registeredScraperManager = Core.SingletonManager.ScraperManager ?? scraperManager;
            
            // 如果 SingletonManager 中没有，说明 Plugin 还没初始化，使用注入的实例
            // 但这种情况不应该发生，因为 ServiceRegistrator 应该在 Plugin 之后初始化
            if (registeredScraperManager == scraperManager)
            {
                _logger.Warn("ServiceRegistrator: SingletonManager.ScraperManager 为空，使用注入的实例。这可能导致 scrapers 未注册。");
                _logger.Info("ServiceRegistrator: 注入的 ScraperManager 内部注册数量={0}", 
                    scraperManager.AllWithNoEnabled().Count);
            }
            else
            {
                _logger.Info("ServiceRegistrator: 使用 Plugin 中已注册 scrapers 的 ScraperManager 实例");
                _logger.Info("ServiceRegistrator: ScraperManager 内部注册数量={0}", 
                    registeredScraperManager.AllWithNoEnabled().Count);
            }
            
            // Initialize SingletonManager（确保使用正确的实例）
            Core.SingletonManager.ScraperManager = registeredScraperManager;
            Core.SingletonManager.JsonSerializer = jsonSerializer;
            Core.SingletonManager.LogManager = logManager;
            Core.SingletonManager.applicationHost = applicationHost;
            
            // Create LibraryManagerEventsHelper instance（使用已注册 scrapers 的实例）
            var libraryManagerEventsHelper = new LibraryManagerEventsHelper(libraryManager, logManager, registeredScraperManager);
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
