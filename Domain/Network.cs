using System;
using System.Collections.Generic;

namespace Domain
{
    //public class Network : APIResponse
    //{
    //    public int Id { get; set; }
    //    public int ZoneId { get; set; }
    //    public int DivisionId { get; set; }
    //    public string Name { get; set; }
    //    public string Description { get; set; }
    //    public string MQTTBasePath { get; set; }
    //    public int MQTTNoOfChannels { get; set; }
    //    public int MQTTActiveChannel { get; set; }
    //    public string MACId { get; set; }
    //    public bool IsActive { get; set; }
    //    public int CreatedBy { get; set; }
    //    public DateTime CreatedDate { get; set; }
    //    public int LastModifiedBy { get; set; }
    //    public DateTime LastModifiedDate { get; set; }
    //    public int? DeletedBy { get; set; }
    //    public DateTime? DeletedDate { get; set; }
    //    public int? SmsFrequency { get; set; }
    //    public int? SmsLimit { get; set; }
    //    public string StationCode { get; set; }
    //    public bool? IsHttpPost { get; set; }
    //    public bool IsSipView { get; set; }
    //    public bool? IsSetupLocalSite { get; set; }
    //    public bool? IsEIEnable { get; set; }
    //    public int? PLCLogId { get; set; }
    //    public bool? IsTest { get; set; }
    //    public bool? IsNoneRE { get; set; }
    //    public bool? IsUnderDevlopment { get; set; }
    //    public bool? IsAnalytics { get; set; }
    //    public bool? IsEarthFaultPushNotification { get; set; }
    //    public int? ChargerDelay { get; set; }
    //    public bool? IsTestUser { get; set; }
    //    public int? RebootTotal { get; set; }
    //    public int? RebootDay { get; set; }
    //    public int? RebootDaynum { get; set; }
    //    public bool? IsCuttingRelayVoltage { get; set; }
    //    public bool? IsSummary { get; set; }
    //    public bool? IsSeriesOpration { get; set; }
    //    public bool? IsPointDetection50v { get; set; }
    //    public System.DateTime? CalibrationStartDate { get; set; }
    //    public System.DateTime? CalibrationEndDate { get; set; }
    //    public System.DateTime? ClusterValidationDate { get; set; }
    //    public string RouterId { get; set; }
    //    public string MobileNumber { get; set; }
    //    public bool IsDatalogger { get; set; }
    //    public bool? IsDataLoggerJsonReverse { get; set; }
    //    public int? SectionId { get; set; }
    //    public int? ModemType { get; set; }
    //    public int? SingleModamType { get; set; }
    //    public int? DataLoggerVersion { get; set; }
    //    public bool? IsCombinePointMachine { get; set; }
    //    public bool? IsFRSAlert { get; set; }
    //    public bool? IsSiemensPoint { get; set; }
    //    //extra
    //    public int UserId { get; set; }
    //    public int RoleId { get; set; }
    //    public bool IsAssignSite { get; set; }
    //    public string ZoneName { get; set; }
    //    public string DivisionName { get; set; }
    //    public string ColumnName { get; set; }
    //    public string SortDirection { get; set; }
    //    public List<string> Channels { get; set; } = new List<string>();
    //    public List<Asset> Assets { get; set; } = new List<Asset>();
    //    public List<ChannelConfig> HttpPostDown { get; set; } = new List<ChannelConfig>();
    //    public List<ChannelConfig> ChannelConfigs { get; set; } = new List<ChannelConfig>();
    //    public List<SMSLog> SMSLogs { get; set; } = new List<SMSLog>();
    //    public List<Probability> Probabilities { get; set; } = new List<Probability>();
    //    public List<Performance> Performances { get; set; } = new List<Performance>();
    //    public List<User> Users { get; set; } = new List<User>();
    //    public int TrackCount { get; set; }
    //    public int SignalCount { get; set; }
    //    public int PointMachineCount { get; set; }
    //    public double RebootTotalValue { get; set; }
    //    public double RebootDayValue { get; set; }
    //    public double RebootDaynumValue { get; set; }
    //    public bool? IsWatchlistRegenerate { get; set; }
    //    public DateTime FromDate { get; set; }
    //    public DateTime ToDate { get; set; }
    //    public bool IsDataloggerStatus { get; set; }
    //    public DateTime? TrainingDate { get; set; }
    //    public int? SiteBenefitTypeId { get; set; }
    //    public int? FRSSiteBenefitCount { get; set; }
    //    public int? SiteBenefitId { get; set; }
    //    public string SiteBenefitRemark { get; set; }
    //    public int? SearchMinutes { get; set; }
    //    public string Type { get; set; }
    //    public bool IsCheckStatus { get; set; }
    //    public Network()
    //    {
    //        ColumnName = "Id";
    //        SortDirection = "DESC";
    //    }

    //    public int ModemUp { get; set; }
    //    public int ModemDown { get; set; }
    //    public int A10Up { get; set; }
    //    public int A10Down { get; set; }
    //    public int MqttUp { get; set; }
    //    public int MqttDown { get; set; }
    //    public bool ModemStatus { get; set; }
    //    public string ModemId { get; set; }
    //    public int SignalStrength { get; set; }
    //    public string ClusterName { get; set; }
    //    //public List<A10VM> A10List { get; set; }



    //}

    //public class NetworkLister : APIResponse
    //{
    //    public NetworkLister()
    //    {
    //        mSites = new List<Site>();
    //        mNetworks = new List<Network>();
    //        SearchCriteria = new Network();
    //        Pager = new Pager();
    //    }

    //    public List<Site> mSites { get; set; }
    //    public List<Network> mNetworks { get; set; }
    //    public Network SearchCriteria { get; set; }
    //    public Pager Pager { get; set; }
    //    public MQTTDetail MQTTDetail { get; set; } = new MQTTDetail();
    //    public HooterSetting HooterSetting { get; set; } = new HooterSetting();

    //}

    public class Network : APIResponse
    {
        public int Id { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string MQTTBasePath { get; set; }
        public int MQTTNoOfChannels { get; set; }
        public int MQTTActiveChannel { get; set; }
        public string MACId { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public int? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }
        public int? SmsFrequency { get; set; }
        public int? SmsLimit { get; set; }
        public string StationCode { get; set; }
        public bool? IsHttpPost { get; set; }
        public bool IsSipView { get; set; }
        public bool? IsSetupLocalSite { get; set; }
        public bool? IsEIEnable { get; set; }
        public int? PLCLogId { get; set; }
        public bool? IsTest { get; set; }
        public bool? IsNoneRE { get; set; }
        public bool? IsUnderDevlopment { get; set; }
        public bool? IsAnalytics { get; set; }
        public bool? IsEarthFaultPushNotification { get; set; }
        public int? ChargerDelay { get; set; }
        public bool? IsTestUser { get; set; }
        public int? RebootTotal { get; set; }
        public int? RebootDay { get; set; }
        public int? RebootDaynum { get; set; }
        public bool? IsCuttingRelayVoltage { get; set; }
        public bool? IsSummary { get; set; }
        public bool? IsSeriesOpration { get; set; }
        public bool? IsPointDetection50v { get; set; }
        public System.DateTime? CalibrationStartDate { get; set; }
        public System.DateTime? CalibrationEndDate { get; set; }
        public System.DateTime? ClusterValidationDate { get; set; }
        public string RouterId { get; set; }
        public string MobileNumber { get; set; }
        public bool IsDatalogger { get; set; }
        public bool? IsDataLoggerJsonReverse { get; set; }
        public int? SectionId { get; set; }
        public int? ModemType { get; set; }
        public int? SingleModamType { get; set; }
        public int? DataLoggerVersion { get; set; }
        public bool? IsCombinePointMachine { get; set; }
        public bool? IsFRSAlert { get; set; }
        public bool? IsSiemensPoint { get; set; }
        //extra
        public int UserId { get; set; }
        public int RoleId { get; set; }
        public bool IsAssignSite { get; set; }
        public string ZoneName { get; set; }
        public string DivisionName { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public List<string> Channels { get; set; } = new List<string>();
        public List<Asset> Assets { get; set; } = new List<Asset>();
        public List<ChannelConfig> HttpPostDown { get; set; } = new List<ChannelConfig>();
        public List<ChannelConfig> ChannelConfigs { get; set; } = new List<ChannelConfig>();
        public List<SMSLog> SMSLogs { get; set; } = new List<SMSLog>();
        public List<Probability> Probabilities { get; set; } = new List<Probability>();
        public List<Performance> Performances { get; set; } = new List<Performance>();
        public List<User> Users { get; set; } = new List<User>();
        public int TrackCount { get; set; }
        public int SignalCount { get; set; }
        public int PointMachineCount { get; set; }
        public double RebootTotalValue { get; set; }
        public double RebootDayValue { get; set; }
        public double RebootDaynumValue { get; set; }
        public bool? IsWatchlistRegenerate { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public bool IsDataloggerStatus { get; set; }
        public DateTime? TrainingDate { get; set; }
        public int? SiteBenefitTypeId { get; set; }
        public int? FRSSiteBenefitCount { get; set; }
        public int? SiteBenefitId { get; set; }
        public string SiteBenefitRemark { get; set; }
        public int? SearchMinutes { get; set; }
        public string Type { get; set; }
        public bool IsCheckStatus { get; set; }
        public Network()
        {
            ColumnName = "Id";
            SortDirection = "DESC";

        }
        public List<Site> mSites { get; set; }
        public Site SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public MQTTDetail MQTTDetail { get; set; } = new MQTTDetail();
        public HooterSetting HooterSetting { get; set; } = new HooterSetting();
    }

    public class NetworkLister : APIResponse
    {
        public NetworkLister()
        {
            mSites = new List<Network>();
            SearchCriteria = new Network();
            Pager = new Pager();
        }
        public List<Network> mSites { get; set; }
        public Network SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public MQTTDetail MQTTDetail { get; set; } = new MQTTDetail();
        public HooterSetting HooterSetting { get; set; } = new HooterSetting();
    }

    public class ModemVM
    {

        public int ClusterId { get; set; }
        public string ModemId { get; set; }
        public string ClusterName { get; set; }
        public string MStatus { get; set; }

        public string Timestamp { get; set; }
        public int SiteId { get; set; }

        public string Duration { get; set; }
        public DateTime? TS { get; set; }

        public string NextTimestamp { get; set; }
        public string TimeRange { get; set; }

        public string A10Status { get; set; }
    }

    public class ModemStatusCountVM
    {
        public int Active { get; set; }
        public int Inactive { get; set; }
    }



    public class A10StatusVM
    {

        public int A10Id { get; set; }
        public string ModemId { get; set; }
        public string ClusterName { get; set; }
        public string A10Status { get; set; }

        public string Timestamp { get; set; }
        public int SiteId { get; set; }

        public string Duration { get; set; }
        public DateTime? TS { get; set; }

        public string TimeRange { get; set; }



    }

    public class A10StatusCountVM
    {
        public int Active { get; set; }
        public int Inactive { get; set; }
    }


}

