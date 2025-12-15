using System;
using System.Threading.Tasks;
using Emby.Plugin.Danmu.Configuration;
using Emby.Plugin.Danmu.Model;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.Danmu
{
    /// <summary>
    /// Plugin startup class that handles library events.
    /// </summary>
    public class PluginStartup : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly LibraryManagerEventsHelper _libraryManagerEventsHelper;
        private readonly ILogger _logger;

        public PluginConfiguration Config
        {
            get
            {
                return Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginStartup"/> class.
        /// </summary>
        /// <param name="libraryManager">The <see cref="ILibraryManager"/>.</param>
        /// <param name="logManager">The <see cref="ILogManager"/>.</param>
        /// <param name="libraryManagerEventsHelper">The <see cref="LibraryManagerEventsHelper"/>.</param>
        public PluginStartup(
            ILibraryManager libraryManager,
            ILogManager logManager,
            LibraryManagerEventsHelper libraryManagerEventsHelper)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger(GetType().Name);
            _libraryManagerEventsHelper = libraryManagerEventsHelper;
        }

        public void Start()
        {
            _libraryManager.ItemAdded += LibraryManagerItemAdded;
            _libraryManager.ItemUpdated += LibraryManagerItemUpdated;
            _logger.Info("Danmu 插件事件监听已启动");
        }

        /// <summary>
        /// Library item was added.
        /// </summary>
        /// <param name="sender">The sending entity.</param>
        /// <param name="itemChangeEventArgs">The <see cref="ItemChangeEventArgs"/>.</param>
        private void LibraryManagerItemAdded(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            if (!Config.DownloadOption.EnableAutoDownload)
            {
                return;
            }

            // Don't do anything if it's not a supported media type
            if (!(itemChangeEventArgs.Item is Movie) && 
                !(itemChangeEventArgs.Item is Episode) && 
                !(itemChangeEventArgs.Item is Series) && 
                !(itemChangeEventArgs.Item is Season))
            {
                return;
            }

            // 当剧集没有SXX/Season XX季文件夹时，LocationType就是Virtual，动画经常没有季文件夹
            if (itemChangeEventArgs.Item.LocationType == LocationType.Virtual && !(itemChangeEventArgs.Item is Season))
            {
                return;
            }

            _libraryManagerEventsHelper.QueueItem(itemChangeEventArgs.Item, EventType.Add);
        }

        /// <summary>
        /// Library item was updated.
        /// </summary>
        /// <param name="sender">The sending entity.</param>
        /// <param name="itemChangeEventArgs">The <see cref="ItemChangeEventArgs"/>.</param>
        private void LibraryManagerItemUpdated(object sender, ItemChangeEventArgs itemChangeEventArgs)
        {
            if (!Config.DownloadOption.EnableAutoDownload)
            {
                return;
            }
            
            // Don't do anything if it's not a supported media type
            if (!(itemChangeEventArgs.Item is Movie) && 
                !(itemChangeEventArgs.Item is Episode) && 
                !(itemChangeEventArgs.Item is Series) && 
                !(itemChangeEventArgs.Item is Season))
            {
                return;
            }

            // 当剧集没有SXX/Season XX季文件夹时，LocationType就是Virtual，动画经常没有季文件夹
            if (itemChangeEventArgs.Item.LocationType == LocationType.Virtual && !(itemChangeEventArgs.Item is Season))
            {
                return;
            }

            _libraryManagerEventsHelper.QueueItem(itemChangeEventArgs.Item, EventType.Update);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Removes event subscriptions on dispose.
        /// </summary>
        /// <param name="disposing"><see cref="bool"/> indicating if object is currently disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _libraryManager.ItemAdded -= LibraryManagerItemAdded;
                _libraryManager.ItemUpdated -= LibraryManagerItemUpdated;
                _libraryManagerEventsHelper?.Dispose();
            }
        }
    }
}

