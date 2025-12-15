using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Emby.Plugin.Danmu.Scrapers.Bilibili.Entity
{
    public class SearchResult
    {
        [JsonPropertyName("result")]
        public List<Media> Result { get; set; }
    }

    public class SearchTypeResult
    {
        [JsonPropertyName("result_type")]
        public string ResultType { get; set; }
        [JsonPropertyName("data")]
        public List<Media> Data { get; set; }
    }
}
