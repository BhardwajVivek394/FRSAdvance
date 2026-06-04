using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class IndexAlertLog
    {
        public DateTime TimeStamp { get; set; }
        public string SiteName { get; set; }
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public string Remarks { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }

        public int UserId { get; set; }
        public int RoleId { get; set; }
    }

    public class IndexAlertLogLister : APIResponse
    {
        public IndexAlertLogLister()
        {
            IndexAlertLogs = new List<IndexAlertLog>();
            SearchCriteria = new IndexAlertLog();
            Pager = new Pager();
            mSites = new List<Site>();
        }
        public List<IndexAlertLog> IndexAlertLogs { get; set; }
        public IndexAlertLog SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<Site> mSites { get; set; }
    }
}
