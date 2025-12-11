using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Emby.Plugin.Danmu.Core.Extensions;

namespace Emby.Plugin.Danmu.Scrapers.Iqiyi.Entity
{
    public class IqiyiAlbumResult
    {
        [JsonPropertyName("data")]
        public IqiyiAlbum Data { get; set; }
    }

}
