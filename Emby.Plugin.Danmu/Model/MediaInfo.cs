namespace Emby.Plugin.Danmu.Model
{
    public class MediaInfo
    {
        public string Id { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Year { get; set; } = string.Empty;

        public int EpisodeSize { get; set; } = 0;

        public string Site { get; set; } = string.Empty;

        public string SiteId { get; set; } = string.Empty;
    }
}

