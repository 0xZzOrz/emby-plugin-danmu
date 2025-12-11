using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Emby.Plugin.Danmu.Scrapers.Tencent.Entity;

public class TencentCommentSegmentResult
{
    [JsonPropertyName("barrage_list")]
    public List<TencentComment> BarrageList { get; set; }
}
