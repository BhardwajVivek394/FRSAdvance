using System;
using System.Collections.Generic;

namespace Domain
{
    public class PointMachineData
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public string Direction { get; set; }
        public string ArrayData { get; set; }
        public string TsTime { get; set; }
        public string Date { get; set; }
        public DateTime TimeStamp { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string AssetName { get; set; }
        public string AssetType { get; set; }
        public int AssetTypeId { get; set; }
        public string Range { get; set; }
        public string SaveType { get; set; }
        public string Type { get; set; }
        public int SiteId { get; set; }
        public decimal Average { get; set; }
        public string Remark { get; set; }
        public string[] Data { get; set; }
        public string AI_status_A { get; set; }
        public string AI_status_B { get; set; }
        public string ClusterA { get; set; }
        public string ClusterB { get; set; }
        public string ChannelLog { get; set; }
        public PointMachineJson PointMachineJson { get; set; } = new PointMachineJson();
       // public string Type { get; set; }
        // Extra
        public bool? IsThickWave { get; set; }
        public string StationCode { get; set; }
        public int? MachineAScale { get; set; }
        public int? MachineBScale { get; set; }
    }

    public class PointMachineDataLister
    {
        public PointMachineDataLister()
        {
            mAssets = new List<Asset>();
            mPointMachineDatas = new List<PointMachineData>();
            SearchCriteria = new PointMachineData();
            Pager = new Pager();
        }
        public List<PointMachineData> mPointMachineDatas { get; set; }
        public PointMachineData SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<Asset> mAssets { get; set; }

    }

    public class PointMachineChannelLog
    {
        public string C { get; set; }
        public string F { get; set; }
        public string M { get; set; }
        public string s { get; set; }
    }
}
