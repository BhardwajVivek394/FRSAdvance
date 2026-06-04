using System;
using System.Collections.Generic;

namespace Domain
{
    public class Asset : APIResponse
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }
        public string Name { get; set; }
        public string Longitude { get; set; }
        public string Latitude { get; set; }
        public string Longitude1 { get; set; }
        public string Latitude1 { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public int? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }
        public bool UnderMaintence { get; set; }
        public string HttpMisLink { get; set; }
        public decimal? ReferenceValue { get; set; }
        public int? IndexScore { get; set; }
        public bool IsAlarm { get; set; }
        public bool? IsHalfPointMachine { get; set; }
        public bool? IsSeriesOpration { get; set; }
        public int? SeriesOprationTypeId { get; set; }
        public string AliasDirectionA { get; set; }
        public string AliasDirectionB { get; set; }
        public bool? IsThickWave { get; set; }
        public int? ThickWaveTypeId { get; set; }
        public bool? IsLux { get; set; }
        public bool? ISDisplacement { get; set; }
        public int? MachineA { get; set; }
        public int? MachineB { get; set; }
        public int? MachineAScale { get; set; }
        public int? MachineBScale { get; set; }
        public int? MachineAReverseScale { get; set; }
        public int? MachineBReverseScale { get; set; }
        public bool? IsSingleModamPartial { get; set; }
        public int? BHMSTypeId { get; set; }
        public int? IPSTypeId { get; set; }
        public bool? IsTemperature { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public decimal? NumberOfCell { get; set; }

        //extra
        public string Zone { get; set; }
        public string Division { get; set; }
        public int NoOfOpration { get; set; }
        public int MQTTNoOfChannels { get; set; }
        public List<SiteAttributeData> siteAttributeDatas { get; set; }
        public List<AssetAttribute> assetAttributes { get; set; }
        public List<AssetInfo> mAssetInfos { get; set; }
        public DateTime SiteDate { get; set; }
        public string SiteName { get; set; }
        public string AssetTypeName { get; set; }
        public string RailwayAssetMake { get; set; }
        public string AssetName { get; set; }
        public int AttributeId { get; set; }
        public string AttributeName { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public bool? IsListView { get; set; }
        public List<string> ChannelList { get; set; }
        public List<string> MQTTPaths { get; set; }
        public List<string> siteAttributeDatasList { get; set; }
        public List<int> SiteIds { get; set; }
        public List<int> AssetIds { get; set; }
        public List<int> AssetTypeIds { get; set; }
        public List<int> ZoneIds { get; set; }
        public List<int> DivisionIds { get; set; }
        public List<FamilyTrack> mFamilyTracks { get; set; }
        public List<string> DateList { get; set; }
        public List<Alert> mAlerts { get; set; }
        public List<Operator> mOperators { get; set; }
        public List<Asset> mAssets { get; set; }

        public List<GlobalConfig> mGlobalConfigs { get; set; }
        public List<MultipleLog> MultipleLog { get; set; } = new List<MultipleLog>();

        public List<AssetMapping> mAssetMappings { get; set; } = new List<AssetMapping>();
        public ErrorCodeIdentifier mErrorCodeIdentifier { get; set; }
        public List<ErrorCode> mErrorCodes { get; set; }

        public decimal ZeroOffsetValue { get; set; }
        public string IValue { get; set; }
        public decimal? Multiplication { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public bool IsMobileView { get; set; }
        public int AttributeCount { get; set; }
        public bool IsReadHttpPost { get; set; }
        public bool IsLocal { get; set; }
        public string DataBaseName { get; set; }
        public bool IsShowDeviceTime { get; set; }
        public string RailwayAssetcode { get; set; }
        public List<PointMachineData> mPointMachineData { get; set; } = new List<PointMachineData>();
        public List<DataloggerAttribute> mDataloggerAttributes { get; set; } = new List<DataloggerAttribute>();
        public List<AssetInfoDatalogger> mAssetInfoDataloggers { get; set; } = new List<AssetInfoDatalogger>();
        public List<BenchMarkingPointMachine> BenchMarkingPointMachines { get; set; } = new List<BenchMarkingPointMachine>();
        public List<Domain.FRSAttributeRange> mFRSAttributeRanges { get; set; } = new List<Domain.FRSAttributeRange>();
        public List<Domain.AlertInfo> mAlertInfos { get; set; } = new List<Domain.AlertInfo>();
        public List<Domain.AlertInfoActivation> mAlertInfoActivations { get; set; } = new List<Domain.AlertInfoActivation>();
        public List<Domain.SignalTrackMapping> mSignalTrackMappings { get; set; } = new List<Domain.SignalTrackMapping>();
        public List<Domain.Datalogger> mDataloggers { get; set; } = new List<Datalogger>();
        public decimal? X { get; set; }
        public decimal? Y { get; set; }
        public decimal? Z { get; set; }
        public bool IsGraphLoad { get; set; }
        public int? Sequence { get; set; }
        public bool IsChecked { get; set; }
        public string ClusterName { get; set; }
        public bool IsOverride { get; set; }
        public bool IsPointMachineGraph { get; set; }
        public bool? IsNoneRE { get; set; }
        public int? TakePointMachineData { get; set; }
        public bool IsMapRG { get; set; }
        public string StationCode { get; set; }
        public int DataloggerAssetId { get; set; }
        public bool IsPointMachineDebug { get; set; }
        public bool IsDatalogger { get; set; }
        public bool? IsCombinePointMachine { get; set; }
        public bool UpdateStatus { get; set; }
        public int RoleId { get; set; }
        public int UserId { get; set; }
        public string MQTTBasePath { get; set; }
        public Asset()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
            siteAttributeDatas = new List<SiteAttributeData>();
            assetAttributes = new List<AssetAttribute>();
            mAssetInfos = new List<AssetInfo>();
            siteAttributeDatasList = new List<string>();
            SiteIds = new List<int>();
            DateList = new List<string>();
            mAlerts = new List<Alert>();
            mOperators = new List<Operator>();
            mAssets = new List<Asset>();
            mFamilyTracks = new List<FamilyTrack>();
            mGlobalConfigs = new List<GlobalConfig>();
            mErrorCodeIdentifier = new ErrorCodeIdentifier();
            mErrorCodes = new List<ErrorCode>();
            mPointMachineData = new List<PointMachineData>();
        }
    }

    public class MultipleLog
    {
        public DateTime TimeStamp { get; set; }
        public string CsvData { get; set; }
        public string AttributeData { get; set; }
        public int AssetId { get; set; }
        public string ActualTimestamp { get; set; }
        public string ChangeTimestamp { get; set; }
    }
    public class AssetLister : APIResponse
    {
        public AssetLister()
        {
            mAssets = new List<Asset>();
            SearchCriteria = new Asset();
            Pager = new Pager();
        }
        public List<Asset> mAssets { get; set; }
        public Asset SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public MQTTDetail MQTTDetail { get; set; } = new MQTTDetail();
        public Site Site { get; set; } = new Site();
    }
}