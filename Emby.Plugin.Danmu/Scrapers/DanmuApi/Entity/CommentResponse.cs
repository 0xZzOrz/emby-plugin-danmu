using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Emby.Plugin.Danmu.Scrapers.DanmuApi.Entity
{
    public class CommentResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("comments")]
        public List<Comment> Comments { get; set; } = new List<Comment>();
    }

    public class Comment
    {
        [JsonPropertyName("cid")]
        public long Cid { get; set; }

        [JsonPropertyName("p")]
        public string P { get; set; } = string.Empty;

        [JsonPropertyName("m")]
        public string M { get; set; } = string.Empty;
    }
}
