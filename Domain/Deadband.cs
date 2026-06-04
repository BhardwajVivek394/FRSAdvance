using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Deadband
    {
        public string AssetName { get; set; }
        public string AttributeName { get; set; }
        public DateTime TimeStamp { get; set; }
        public int SiteId { get; set; }


        //extra
        public int AssetTypeId { get; set; }
        public string AssetTypeName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime Date { get; set; }
        public TimeSpan Time { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal PreviousValue { get; set; }
        public Deadband()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class DeadbandLister : APIResponse
    {
        public DeadbandLister()
        {
            mDeadbands = new List<Deadband>();
            SearchCriteria = new Deadband();
            Pager = new Pager();
        }
        public List<Deadband> mDeadbands { get; set; }
        public Deadband SearchCriteria { get; set; }
        public Pager Pager { get; set; }

    }
}
