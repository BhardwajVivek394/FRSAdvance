using System;
using System.Collections.Generic;

namespace Domain
{
    public class JobCardOverallTarget
    {
        public int Id { get; set; }
        public int JobCardId { get; set; }
        public int TypeId { get; set; }
        public bool IsResolved { get; set; }
        public int? AssetId { get; set; }
        public int? AssetTypeId { get; set; }
        public int? AttributeId { get; set; }
        public int? WatchListId { get; set; }
        public int CreatedBy { get; set; }
        public bool? IsAdminResolved { get; set; }
        public int? FRSWatchListId { get; set; }
        public int? A10Id { get; set; }
        public string ClusterName { get; set; }
        public string Reason { get; set; }
        public string MatrialCode { get; set; }

        //Extra
        public string Cluster { get; set; }
        public int? ADCId { get; set; }
        public string Card { get; set; }
        public int? Pin { get; set; }
        public string Value { get; set; }
        public int SiteId { get; set; }
        public string SiteName { get; set; }
        public string AssetTypeName { get; set; }
        public string AssetName { get; set; }
        public string AttributeName { get; set; }
        public string IssueType { get; set; }
        public string Message { get; set; }
        public DateTime TimeStamp { get; set; }
        public System.DateTime? ResolvedDate { get; set; }
        public string Remark { get; set; }
        public string CauseCode { get; set; }
        public string Description { get; set; }
        public List<JobCardOverallTargetRemark> mJobCardOverallTargetRemarks { get; set; } = new List<JobCardOverallTargetRemark>();
    }
}
