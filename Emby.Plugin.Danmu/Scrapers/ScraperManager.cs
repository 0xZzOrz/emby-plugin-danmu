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
            log.Info("ScraperManager.All() 调用: 内部注册的 scrapers 数量={0}", _scrapers.Count);
            
            // 存在配置时，根据配置调整源顺序，并删除不启用的源
            if (Plugin.Instance?.Configuration.Scrapers != null)
            {
                var orderScrapers = new List<AbstractScraper>();

                var scraperMap = this._scrapers.ToDictionary(x => x.Name, x => x);
                var configScrapers = Plugin.Instance.Configuration.Scrapers;
                
                log.Info("ScraperManager.All() 配置中的 scrapers 数量={0}", configScrapers.Length);
                
                foreach (var config in configScrapers)
                {
                    log.Info("ScraperManager.All() 检查配置: Name={0}, Enable={1}, 是否在注册列表中={2}", 
                        config.Name, config.Enable, scraperMap.ContainsKey(config.Name));
                    
                    if (scraperMap.ContainsKey(config.Name) && config.Enable)
                    {
                        orderScrapers.Add(scraperMap[config.Name]);
                        log.Info("ScraperManager.All() 添加启用的 scraper: {0}", config.Name);
                    }
                }

                // 添加新增并默认启用的源
                var allOldScaperNames = configScrapers.Select(o => o.Name).ToList();
                foreach (var scraper in this._scrapers)
                {
                    if (!allOldScaperNames.Contains(scraper.Name) && scraper.DefaultEnable)
                    {
                        orderScrapers.Add(scraper);
                        log.Info("ScraperManager.All() 添加默认启用的新 scraper: {0}", scraper.Name);
                    }
                }

                log.Info("ScraperManager.All() 最终返回的 scrapers 数量={0}", orderScrapers.Count);
                return orderScrapers.AsReadOnly();
            }

            log.Info("ScraperManager.All() 无配置，返回所有注册的 scrapers 数量={0}", _scrapers.Count);
            return this._scrapers.AsReadOnly();
        }

        public ReadOnlyCollection<AbstractScraper> AllWithNoEnabled()
        {
            return this._scrapers.AsReadOnly();
        }
    }
}
