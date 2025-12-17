using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.Danmu.Scrapers
{
    public class ScraperManager
    {
        protected ILogger log;
        private List<AbstractScraper> _scrapers = new List<AbstractScraper>();

        public ScraperManager(ILogManager logManager)
        {
            log = logManager.GetLogger(GetType().Name);
        }

        public void Register(AbstractScraper scraper)
        {
            this._scrapers.Add(scraper);
        }

        public void Register(IList<AbstractScraper> scrapers)
        {
            this._scrapers.AddRange(scrapers);
        }

        public ReadOnlyCollection<AbstractScraper> All()
        {
            // 仅保留简要日志，避免占用过多日志空间
            log.Debug("ScraperManager.All() 内部注册={0}", _scrapers.Count);
            
            // 存在配置时，根据配置调整源顺序，并删除不启用的源
            if (Plugin.Instance?.Configuration.Scrapers != null)
            {
                var orderScrapers = new List<AbstractScraper>();

                var scraperMap = this._scrapers.ToDictionary(x => x.Name, x => x);
                var configScrapers = Plugin.Instance.Configuration.Scrapers;
                
                log.Debug("ScraperManager.All() 配置项数量={0}", configScrapers.Length);
                
                foreach (var config in configScrapers)
                {
                    if (scraperMap.ContainsKey(config.Name) && config.Enable)
                    {
                        orderScrapers.Add(scraperMap[config.Name]);
                    }
                }

                // 添加新增并默认启用的源
                var allOldScaperNames = configScrapers.Select(o => o.Name).ToList();
                foreach (var scraper in this._scrapers)
                {
                    if (!allOldScaperNames.Contains(scraper.Name) && scraper.DefaultEnable)
                    {
                        orderScrapers.Add(scraper);
                    }
                }

                log.Debug("ScraperManager.All() 返回启用的 scrapers 数量={0}", orderScrapers.Count);
                return orderScrapers.AsReadOnly();
            }

            log.Debug("ScraperManager.All() 无配置，返回所有注册的 scrapers 数量={0}", _scrapers.Count);
            return this._scrapers.AsReadOnly();
        }

        public ReadOnlyCollection<AbstractScraper> AllWithNoEnabled()
        {
            return this._scrapers.AsReadOnly();
        }
    }
}
