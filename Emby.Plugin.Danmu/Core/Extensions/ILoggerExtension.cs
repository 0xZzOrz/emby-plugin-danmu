using System;
using Emby.Plugin.Danmu.Core.Extensions;
using System.Linq;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.Danmu.Core.Extensions
{
    public static class ILoggerExtension
    {
        public static ILogger GetLogger<T>(this ILogManager logManager)
        {
            return logManager.GetLogger(typeof(T).Name);
        }

        public static void LogErrorException(this ILogger logger, string message, Exception ex, params object[] args)
        {
            logger.ErrorException(message, ex, args);
        }

        public static void LogErrorException(this ILogger logger, string message, params object[] args)
        {
            logger.Error(message, args);
        }

        public static void LogInformation(this ILogger logger, string message, params object[] args)
        {
            logger.Info(message, args);
        }

        public static void LogDebug(this ILogger logger, string message, params object[] args)
        {
            logger.Debug(message, args);
        }

        public static void LogWarning(this ILogger logger, string message, params object[] args)
        {
            logger.Warn(message, args);
        }

        public static void LogError(this ILogger logger, string message, params object[] args)
        {
            logger.Error(message, args);
        }

        public static void LogError(this ILogger logger, Exception ex, string message, params object[] args)
        {
            logger.ErrorException(message, ex, args);
        }
    }
}

