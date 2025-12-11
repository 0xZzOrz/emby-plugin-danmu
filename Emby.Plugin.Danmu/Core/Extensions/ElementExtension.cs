// using AngleSharp.Dom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Entities;

namespace Emby.Plugin.Danmu.Core.Extensions
{
    public static class ElementExtension
    {
        /// <summary>
        /// Try to get provider ID from item.
        /// </summary>
        public static bool TryGetProviderId(this BaseItem item, string providerId, out string? value)
        {
            value = item.GetProviderId(providerId);
            return !string.IsNullOrEmpty(value);
        }

        /// <summary>
        /// Get seasons from Series (Emby compatibility).
        /// </summary>
        public static IEnumerable<Season> GetSeasons(this Series series, ILibraryManager libraryManager, DtoOptions options)
        {
            // In Emby, use GetItemList with Path filter or get children directly
            // Since Series.GetSeasons doesn't exist, we'll get all items and filter
            var allSeasons = libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Season" }
            }).OfType<Season>();
            
            // Filter by parent series
            return allSeasons.Where(s => s.GetParent()?.Id == series.Id);
        }
        // public static string? GetText(this IElement el, string css)
        // {
        //     var node = el.QuerySelector(css);
        //     if (node != null)
        //     {
        //         return node.Text().Trim();
        //     }

        //     return null;
        // }

        // public static string GetTextOrDefault(this IElement el, string css, string defaultVal = "")
        // {
        //     var node = el.QuerySelector(css);
        //     if (node != null)
        //     {
        //         return node.Text().Trim();
        //     }

        //     return defaultVal;
        // }

        // public static string? GetAttr(this IElement el, string css, string attr)
        // {
        //     var node = el.QuerySelector(css);
        //     if (node != null)
        //     {
        //         var attrVal = node.GetAttribute(attr);
        //         return attrVal != null ? attrVal.Trim() : null;
        //     }

        //     return null;
        // }

        // public static string? GetAttrOrDefault(this IElement el, string css, string attr, string defaultVal = "")
        // {
        //     var node = el.QuerySelector(css);
        //     if (node != null)
        //     {
        //         var attrVal = node.GetAttribute(attr);
        //         return attrVal != null ? attrVal.Trim() : defaultVal;
        //     }

        //     return defaultVal;
        // }
    }
}