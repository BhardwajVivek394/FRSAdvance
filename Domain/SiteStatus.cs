using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class SiteStatus
    {
        public string SiteName { get; set; }
        public int SiteId { get; set; }
        public string Channel { get; set; }
        public string Mac { get; set; }
        public DateTime TimeStamp { get; set; }

        public string Hours { get; set; }
        public List<string> HoursList { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public int UserId { get; set; }
        public int RoleId { get; set; }
        public SiteStatus()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }

    }

    public class SiteStatusLister : APIResponse
    {
        public SiteStatusLister()
        {
            mSiteStatus = new List<SiteStatus>();
            SearchCriteria = new SiteStatus();
            Pager = new Pager();
        }
        public List<SiteStatus> mSiteStatus { get; set; }
        public SiteStatus SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
