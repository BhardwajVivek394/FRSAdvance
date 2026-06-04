using System;
using System.Collections.Generic;

namespace Domain
{
    public class FRSAlert
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AlertInfoId { get; set; }
        public int AlertCategoryId { get; set; }
        public string SetAlertMessage { get; set; }
        public System.DateTime SetTimeStamp { get; set; }
        public string ResetAlertMessage { get; set; }
        public System.DateTime? ResetTimeStamp { get; set; }
        public string Description { get; set; }
        public string PossibleCause { get; set; }
        public string PossibleSolution { get; set; }
        public string CountOfPastTrend { get; set; }
        public string ThresholdBreach { get; set; }
        public int AcknowledgemenStatusId { get; set; }
        public int ResponsiblePersonId { get; set; }
        public int? UserClassId { get; set; }
        public string MobileNumber { get; set; }
        public bool IsActive { get; set; }
        public int AlertTypeId { get; set; }
        public string CauseCode { get; set; }
        public System.DateTime? AcknowledgemenTimeStamp { get; set; }
        public DateTime? RectificationDateTime { get; set; }
        public string Remark { get; set; }
        public string AlertInsight { get; set; }
        public int? PTAlertInfoId { get; set; }
        public int? MaintainerId { get; set; }

        //Extra
        public string StationCode { get; set; }
        public string ZoneName { get; set; }
        public string DivisionName { get; set; }
        public string SiteName { get; set; }
        public string AssetName { get; set; }
        public string AssetType { get; set; }
        public string Code { get; set; }
        public int AssetTypeId { get; set; }
        public int DivisionId { get; set; }
        public int ZoneId { get; set; }
        public string MaintainerName { get; set; }
        public string MaintainerDesignation { get; set; }
        public string MaintainerMobileNumber { get; set; }
        public string MaintainerRemarks { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public TimeSpan FromTime { get; set; }
        public TimeSpan ToTime { get; set; }
        public int AlertStatus { get; set; }
        public int GraphType { get; set; }
        public string AlertType { get; set; }
        public bool? IsAcknowledgement { get; set; }
        public bool IsAutoRefresh { get; set; }
        public string SectionName { get; set; }
        public int? SectionId { get; set; }
        public int AlertInstanceCount { get; set; }
        public string TempRemark { get; set; }
        public int RoleId { get; set; }
        public int UserId { get; set; }
        public string CurrentTab { get; set; }
        public bool? IsSetWatchList { get; set; }
        public bool? IsRailwayUpdate { get; set; }
        public string DistributeName { get; set; }
        public int? DistributeUserId { get; set; }
        public List<AlertInfo> mAlertInfos { get; set; } = new List<AlertInfo>();
        public List<int> ZoneIds { get; set; }
        public List<int> DivisionIds { get; set; }
        public List<int> SiteIds { get; set; }
        public List<int> AssetTypeIds { get; set; }
        public List<int> AlertTypeIds { get; set; }
        public List<int> AcknowledgemenStatusIds { get; set; }
        public List<int> AlertInfoIds { get; set; }
        public int isManual { get; set; }
        public string RawJsonData { get; set; }
        public string ResetRawJsonData { get; set; }
        public string PhotoEvidence { get; set; }
        public string PhotoEvidenceBase64 { get; set; }
    }


    public class FRSAlertLister
    {
        public FRSAlertLister()
        {
            AssetTypes = new List<AssetType>();
            mFRSAlerts = new List<FRSAlert>();
            SearchCriteria = new FRSAlert();
            Pager = new Pager();
        }
        public List<FRSAlert> mFRSAlerts { get; set; }
        public List<Domain.AssetType> AssetTypes { get; set; }
        public FRSAlert SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public int PredictiveCount { get; set; }
        public int FailCount { get; set; }
        public int FailurePendingCount { get; set; }
        public int FailureClearedCount { get; set; }
        public int PredictivePendingCount { get; set; }
        public int PredictiveClearedCount { get; set; }
    }
}
