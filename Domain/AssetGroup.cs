using System.Collections.Generic;

namespace Domain
{
    public class AssetGroup
    {
        public int AssetId { get; set; }
        public int AssetTypeId { get; set; }
        public string AssetName { get; set; }
        public string SiteName { get; set; }
        public int SiteId { get; set; }
        public List<Performance> mPerformances { get; set; }
        public List<Probability> mProbabilities { get; set; }
        public List<SMSLog> mSMSLogs { get; set; }
        public List<Asset> mAssets { get; set; }

        public AssetGroup()
        {
            mPerformances = new List<Performance>();
            mProbabilities = new List<Probability>();
            mSMSLogs = new List<SMSLog>();
            mAssets = new List<Asset>();
        }
    }
}
