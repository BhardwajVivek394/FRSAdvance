using System.Collections.Generic;

namespace Domain
{
    public class DataloggerAsset
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int DataloggerAssetTypeId { get; set; }
        public string Name { get; set; }
        public int CreatedBy { get; set; }

        //Extra
        public int SrNo { get; set; }
        public string RelayType { get; set; }
        public string RelayName { get; set; }
        public string ContactType { get; set; }
        public int DataloggerAttributeId { get; set; }
        public string AttributeName { get; set; }
        public string AssetTypeName { get; set; }
        public List<string> MQTTPaths { get; set; } = new List<string>();
        public List<DataloggerInfo> mDataloggerInfos { get; set; } = new List<DataloggerInfo>();
        public List<Domain.DataloggerAttribute> mDataloggerAttributes { get; set; } = new List<Domain.DataloggerAttribute>();
    }

    public class DataloggerAssetLister
    {
        public DataloggerAssetLister()
        {
            DataloggerAssets = new List<DataloggerAsset>();
            SearchCriteria = new DataloggerAsset();
            Pager = new Pager();
            MQTTDetail = new MQTTDetail();
        }
        public List<DataloggerAsset> DataloggerAssets { get; set; }
        public DataloggerAsset SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public MQTTDetail MQTTDetail { get; set; }
    }
}
