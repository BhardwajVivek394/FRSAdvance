using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class RFStatus
    {
        public string SiteName { get; set; }
        public string DeviceName { get; set; }
        public string A10Id { get; set; }
        public string LastAccesstime { get; set; }
        public DateTime TimeStamp { get; set; }
        public int CardLineId { get; set; }

        //Extra
        public string Hours { get; set; }
        public List<string> HoursList { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public int SiteId { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public int UserId { get; set; }
        public int RoleId { get; set; }
        //Extra
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public RFStatus()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class RFStatusLister 
    {
        public RFStatusLister()
        {
            mRFStatus = new List<RFStatus>();
            SearchCriteria = new RFStatus();
            Pager = new Pager();
        }
        public List<RFStatus> mRFStatus { get; set; }
        public RFStatus SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
