using System;
using System.Collections.Generic;

namespace Domain
{
    public class RDPMSHealthLive
    {
        public int ZoneId { get; set; }
        public string ModemIdStr { get; set; }
        public string A10IdStr { get; set; }
        public int DivisionId { get; set; }
        public int AssetId { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }
        public string Name { get; set; }
        public string AssetTypeName { get; set; }
        public string SiteName { get; set; }
        public string Zone { get; set; }
        public string Division { get; set; }
        public int TotalSensors { get; set; }
        public int TotalIoTSensors { get; set; }
        public int TotalNetworkSensors { get; set; }
        public int TotalStnGateway { get; set; }

        public List<int> ZoneIds { get; set; }
        public List<int> DivisionIds { get; set; }
        public List<int> SiteIds { get; set; }
        public List<int> AssetTypeIds { get; set; }
        public List<int> AlertTypeIds { get; set; }
        public int UserId { get; set; }
        public int RoleId { get; set; }
        public DateTime FromDate { get; set; }
        public string FromTime { get; set; }
        public DateTime ToDate { get; set; }
        public string ToTime { get; set; }

    }

    public class RDPMSHealthLiveLister
    {
        public RDPMSHealthLiveLister()
        {
            mClusters = new List<Domain.Cluster>();
            mADCs = new List<Domain.ADC>();
            mAssetInfos = new List<AssetInfo>();
            GatewaySiteId = new List<int>();
            mModemHistories = new List<Dto.ModemHistory>();
            mA10Histories = new List<Dto.ModemHistory>();

            mAssets = new List<RDPMSHealthLive>();
            SearchCriteria = new RDPMSHealthLive();
            Pager = new Pager();
        }
        public List<RDPMSHealthLive> mAssets { get; set; }
        public RDPMSHealthLive SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<Domain.Cluster> mClusters { get; set; }
        public List<Domain.ADC> mADCs { get; set; }
        public List<AssetInfo> mAssetInfos { get; set; }
        public List<int> GatewaySiteId { get; set; }
        public List<Domain.Dto.ModemHistory> mModemHistories { get; set; }
        public List<Domain.Dto.ModemHistory> mA10Histories { get; set; }

    }


}
