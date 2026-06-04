using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class PMDataTable
    {
        public DateTime TimeStamp { get; set; }
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public string Direction { get; set; }
        public string CurrentPostion { get; set; }
        public string CurrentValue { get; set; }
        public string LockedValue { get; set; }
        public int SiteId { get; set; }

        public bool IsApp { get; set; }
        public PointMachineData PointMachineData { get; set; }
    }
    public class PMDataTableLister : APIResponse
    {
        public PMDataTableLister()
        {
            PMDataTables = new List<PMDataTable>();
            SearchCriteria = new PMDataTable();
            Pager = new Pager();
            Assets = new List<Asset>();
            Direction = new List<string>();
        }
        public List<PMDataTable> PMDataTables { get; set; }
        public PMDataTable SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<Asset> Assets { get; set; }
        public List<string> Direction { get; set; }
        public string Remark { get; set; }
    }
}
