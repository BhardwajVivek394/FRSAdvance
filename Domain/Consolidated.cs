using System.Collections.Generic;

namespace Domain
{
    public class Consolidated
    {
        public string Year { get; set; }
        public string[] Month { get; set; }
        public int AssetTypeId { get; set; }
        public int SiteId { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }

        //Extra
        public string AssetType { get; set; }
        public int AssetCount { get; set; }
        public int AlertCount { get; set; }
        public int ProbabilityCount { get; set; }
    }

    public class ConsolidatedLister
    {
        public ConsolidatedLister()
        {
            Consolidated = new List<Consolidated>();
            SearchCriteria = new Consolidated();
            Pager = new Pager();
            AssetTypes = new List<AssetType>();
            mSMSLogs = new List<SMSLog>();
            Probability = new List<Probability>();
        }
        public List<Consolidated> Consolidated { get; set; }
        public Consolidated SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<AssetType> AssetTypes { get; set; }
        public List<SMSLog> mSMSLogs { get; set; }
        public List<Probability> Probability { get; set; }
    }
}
