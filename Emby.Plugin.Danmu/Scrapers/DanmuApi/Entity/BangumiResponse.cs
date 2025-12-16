using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Emby.Plugin.Danmu.Scrapers.DanmuApi.Entity
{
    /// <summary>
    /// JSON 转换器：将数字或字符串转换为字符串
    /// </summary>
    public class StringOrNumberConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                // 如果是数字，转换为字符串
                if (reader.TryGetInt64(out long longValue))
                {
                    return longValue.ToString();
                }
                if (reader.TryGetDouble(out double doubleValue))
                {
                    return ((long)doubleValue).ToString();
                }
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                // 如果已经是字符串，直接返回
                return reader.GetString() ?? string.Empty;
            }
            
            throw new JsonException($"无法将 {reader.TokenType} 转换为字符串");
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
    public class BangumiResponse
    {
        [JsonPropertyName("errorCode")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; }

        [JsonPropertyName("bangumi")]
        public Bangumi? Bangumi { get; set; }
    }

    public class Bangumi
    {
        [JsonPropertyName("animeId")]
        public long AnimeId { get; set; }

        [JsonPropertyName("bangumiId")]
        public string BangumiId { get; set; } = string.Empty;

        [JsonPropertyName("animeTitle")]
        public string AnimeTitle { get; set; } = string.Empty;

        [JsonPropertyName("episodes")]
        public List<Episode> Episodes { get; set; } = new List<Episode>();

        /// <summary>
        /// 按平台分组的剧集列表，保持原始顺序
        /// </summary>
        [JsonIgnore]
        public List<EpisodeGroup> EpisodeGroups
        {
            get
            {
                var groups = new List<EpisodeGroup>();
                var currentPlatform = string.Empty;
                var currentEpisodes = new List<Episode>();
                
                foreach (var episode in Episodes)
                {
                    var platform = episode.Platform ?? string.Empty;
                    
                    if (platform != currentPlatform)
                    {
                        if (currentEpisodes.Count > 0)
                        {
                            groups.Add(new EpisodeGroup 
                            { 
                                Platform = currentPlatform, 
                                Episodes = currentEpisodes 
                            });
                        }
                        
                        currentPlatform = platform;
                        currentEpisodes = new List<Episode>();
                    }
                    
                    currentEpisodes.Add(episode);
                }
                
                // 添加最后一组
                if (currentEpisodes.Count > 0)
                {
                    groups.Add(new EpisodeGroup 
                    { 
                        Platform = currentPlatform, 
                        Episodes = currentEpisodes 
                    });
                }
                
                return groups;
            }
        }
    }

    public class EpisodeGroup
    {
        public string Platform { get; set; } = string.Empty;
        public List<Episode> Episodes { get; set; } = new List<Episode>();
    }

    public class Episode
    {
        private static readonly Regex PlatformRegex = new Regex(@"【(.+?)】", RegexOptions.Compiled);

        [JsonPropertyName("seasonId")]
        public string SeasonId { get; set; } = string.Empty;

        // API 返回的是数字类型，但我们需要字符串形式
        // 使用自定义转换器处理数字和字符串两种情况
        [JsonPropertyName("episodeId")]
        [JsonConverter(typeof(StringOrNumberConverter))]
        public string EpisodeId { get; set; } = string.Empty;

        [JsonPropertyName("episodeTitle")]
        public string EpisodeTitle { get; set; } = string.Empty;

        [JsonPropertyName("episodeNumber")]
        public string EpisodeNumber { get; set; } = string.Empty;

        /// <summary>
        /// 从 EpisodeTitle 中解析平台标识，格式如：【qq】 第1集
        /// </summary>
        [JsonIgnore]
        public string? Platform
        {
            get
            {
                if (string.IsNullOrEmpty(EpisodeTitle))
                {
                    return null;
                }

                var match = PlatformRegex.Match(EpisodeTitle);
                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value.Trim();
                }

                return null;
            }
        }
    }
}
