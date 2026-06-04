using System;
using System.Collections.Generic;

namespace Domain
{
    public class FRSAlertAudit
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AlertInfoId { get; set; }
        public string Remark { get; set; }
        public string RailwayRemark { get; set; }
        public string MaintainerRemark { get; set; }
        public bool IsMaintainerAlert { get; set; }
        public bool? IsRailwayAlert { get; set; }
        public bool IsAlert { get; set; }
        public bool IsOpenClose { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime LastModifiedDate { get; set; }
        public int? IssueTypeId { get; set; }
        public int FRSAlertId { get; set; }
        public int? ResolvedBy { get; set; }
        public System.DateTime? ResolvedDate { get; set; }
        public string ResolvedRemark { get; set; }
        public bool WorngCauseCode { get; set; }
        public System.DateTime? AlertDate { get; set; }

        //Extra

        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public string AlertMessage { get; set; }
        public string CauseCode { get; set; }
        public bool IsResolved { get; set; }
        public string Site { get; set; }
        public string Asset { get; set; }
        public string AssetType { get; set; }
        public int AssetTypeId { get; set; }
        public DateTime CreatedDate { get; set; }
        public string LastModifiedName { get; set; }
        public string CreatedName { get; set; }
        public System.DateTime? TimeStamp { get; set; }
        public string ResolvedName { get; set; }
        public List<Domain.FRSWatchListAttribute> FRSWatchListAttributes { get; set; } = new List<Domain.FRSWatchListAttribute>();
    }
}
