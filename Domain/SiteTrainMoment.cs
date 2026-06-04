using System;
using System.Collections.Generic;

namespace Domain
{
    public class SiteTrainMoment
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

        public List<Asset> UpLineAssets { get; set; } = new List<Asset>();
        public List<Asset> DownLineAssets { get; set; } = new List<Asset>();
        public string DownAsset { get; set; }
        public string UpAsset { get; set; }
    }
}
