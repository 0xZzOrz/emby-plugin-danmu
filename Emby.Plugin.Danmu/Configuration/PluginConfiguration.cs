using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

namespace Emby.Plugin.Danmu.Configuration
{
    /// <summary>
    /// Plugin configuration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {

        /// <summary>
        /// 版本信息
        /// </summary>
        [DisplayName("版本")]
        [Description("插件版本号")]
        [ReadOnly(true)]
        public string Version { get; set; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        /// <summary>
        /// 检测弹幕数和视频剧集数需要一致才自动下载弹幕.
        /// </summary>
        [DisplayName("弹幕下载配置")]
        [Description("配置弹幕自动下载的相关选项")]
        public DanmuDownloadOption DownloadOption { get; set; } = new DanmuDownloadOption();

        /// <summary>
        /// 弹弹play配置
        /// </summary>
        [DisplayName("弹弹play配置")]
        [Description("配置弹弹play相关的选项")]
        public DandanOption Dandan { get; set; } = new DandanOption();

        /// <summary>
        /// 弹幕 API 配置
        /// </summary>
        [DisplayName("弹幕API配置")]
        [Description("配置弹幕API服务器相关选项")]
        public DanmuApiOption DanmuApi { get; set; } = new DanmuApiOption();

        /// <summary>
        /// 是否同时生成ASS格式弹幕.
        /// </summary>
        [DisplayName("同时生成ASS格式弹幕")]
        [Description("勾选后，会在视频目录下生成ass格式的弹幕，命名格式：[视频名].danmu.ass")]
        public bool ToAss { get; set; } = false;

        /// <summary>
        /// 字体.
        /// </summary>
        [DisplayName("ASS弹幕字体")]
        [Description("可为空，默认黑体")]
        public string AssFont { get; set; } = string.Empty;

        /// <summary>
        /// 字体大小.
        /// </summary>
        [DisplayName("ASS弹幕字体大小")]
        [Description("可为空，默认60，可以此为基准，增大或缩小")]
        public string AssFontSize { get; set; } = string.Empty;

        /// <summary>
        /// 透明度.
        /// </summary>
        [DisplayName("ASS弹幕字体透明度")]
        [Description("可为空，默认1，表示不透明，数值在0.0~1.0之间")]
        public string AssTextOpacity { get; set; } = string.Empty;

        /// <summary>
        /// 限制行数.
        /// </summary>
        [DisplayName("ASS弹幕显示行数")]
        [Description("可为空，默认全屏显示，1/4屏可填5，半屏可填9")]
        public string AssLineCount { get; set; } = string.Empty;

        /// <summary>
        /// 移动速度.
        /// </summary>
        [DisplayName("ASS弹幕移动速度")]
        [Description("可为空，默认8秒")]
        public string AssSpeed { get; set; } = string.Empty;

        /// <summary>
        /// 删除 emoji 表情。
        /// </summary>
        [DisplayName("删除emoji表情")]
        [Description("是否在生成ASS时删除emoji表情")]
        public bool AssRemoveEmoji { get; set; } = true;

        /// <summary>
        /// 弹幕源.
        /// </summary>
        private List<ScraperConfigItem> _scrapers;

        [XmlArrayItem(ElementName = "Scraper")]
        [DisplayName("弹幕源列表")]
        [Description("已配置的弹幕源列表")]
        public ScraperConfigItem[] Scrapers
        {
            get
            {
                var defaultScrapers = new List<ScraperConfigItem>();
                if (Plugin.Instance?.Scrapers != null)
                {
                    foreach (var scaper in Plugin.Instance.Scrapers)
                    {
                        defaultScrapers.Add(new ScraperConfigItem(scaper.Name, scaper.DefaultEnable));
                    }
                }

                if (_scrapers?.Any() != true)
                {
                    // 没旧配置，返回默认列表
                    return defaultScrapers.ToArray();
                }
                else
                {
                    // 已保存有配置

                    // 删除已废弃的插件配置
                    var allValidScaperNames = defaultScrapers.Select(o => o.Name).ToList();
                    _scrapers.RemoveAll(o => !allValidScaperNames.Contains(o.Name));

                    // 找出新增的插件
                    var oldScrapers = _scrapers.Select(o => o.Name).ToList();
                    defaultScrapers.RemoveAll(o => oldScrapers.Contains(o.Name));

                    // 合并新增的scrapers
                    _scrapers.AddRange(defaultScrapers);
                }
                return _scrapers.ToArray();
            }
            set
            {
                _scrapers = value.ToList();
            }
        }
    }

    /// <summary>
    /// 弹幕源配置
    /// </summary>
    [DisplayName("弹幕源配置项")]
    public class ScraperConfigItem
    {
        [DisplayName("启用")]
        [Description("是否启用此弹幕源")]
        public bool Enable { get; set; }

        [DisplayName("名称")]
        [Description("弹幕源名称")]
        [ReadOnly(true)]
        public string Name { get; set; }

        public ScraperConfigItem()
        {
            this.Name = "";
            this.Enable = false;
        }

        public ScraperConfigItem(string name, bool enable)
        {
            this.Name = name;
            this.Enable = enable;
        }
    }

    [DisplayName("弹幕下载选项")]
    public class DanmuDownloadOption
    {
        /// <summary>
        /// 弹幕自动匹配下载.
        /// </summary>
        [DisplayName("弹幕自动匹配下载")]
        [Description("勾选后，有新影片入库会自动匹配下载，不勾选需要自己搜索下载")]
        public bool EnableAutoDownload { get; set; } = true;
        
        /// <summary>
        /// 检测弹幕数和视频剧集数需要一致才自动下载弹幕.
        /// </summary>
        [DisplayName("弹幕总数需和电视剧总集数一致")]
        [Description("勾选后，假如匹配了错误的电视剧，可以避免自动下载错误的弹幕")]
        public bool EnableEpisodeCountSame { get; set; } = true;
    }

    /// <summary>
    /// 弹弹play配置
    /// </summary>
    [DisplayName("弹弹play选项")]
    public class DandanOption
    {
        /// <summary>
        /// 同时获取关联的第三方弹幕
        /// </summary>
        [DisplayName("同时获取关联的第三方弹幕")]
        [Description("勾选后，返回此弹幕库对应的所有第三方关联网址的弹幕")]
        public bool WithRelatedDanmu { get; set; } = true;

        /// <summary>
        /// 中文简繁转换。0-不转换，1-转换为简体，2-转换为繁体
        /// </summary>
        [DisplayName("弹幕中文简繁转换")]
        [Description("选择弹幕的简繁转换方式")]
        public int ChConvert { get; set; } = 0;

        /// <summary>
        /// 使用文件哈希值进行匹配.
        /// </summary>
        [DisplayName("使用文件哈希值进行匹配")]
        [Description("勾选后，搜索和匹配的成功率和精确度会更高，但会消耗额外的计算资源，当视频文件存储在远端时还会占用网络带宽")]
        public bool MatchByFileHash { get; set; } = false;
    }

    /// <summary>
    /// 弹幕 API 配置
    /// </summary>
    [DisplayName("弹幕API选项")]
    public class DanmuApiOption
    {
        /// <summary>
        /// 弹幕 API 服务器地址（带 http/https 的 BaseURL）
        /// </summary>
        [DisplayName("API服务器地址")]
        [Description("填写API服务器的完整地址（包含 http:// 或 https://），部署推荐: danmu_api")]
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// 允许的平台列表（多个平台用逗号分隔，如：bilibili,tencent,youku）。为空则不限制
        /// </summary>
        [DisplayName("允许的平台")]
        [Description("限制只从指定平台获取弹幕，多个平台用逗号分隔，留空则不限制平台")]
        public string AllowedPlatforms { get; set; } = string.Empty;

        /// <summary>
        /// 允许的采集源列表（多个采集源用逗号分隔，如：dandan,mikan,dmhy）。为空则不限制
        /// </summary>
        [DisplayName("允许的采集源")]
        [Description("限制只从指定采集源获取弹幕，多个采集源用逗号分隔，留空则不限制采集源")]
        public string AllowedSources { get; set; } = string.Empty;
    }
}

