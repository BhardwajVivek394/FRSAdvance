using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    //public class PerformanceAlert
    //{
    //    // ── Primary Key ──────────────────────────────────────────────
    //    public int Id { get; set; }

    //    // ── Foreign Keys ─────────────────────────────────────────────
    //    public int SiteId { get; set; }
    //    public int AssetId { get; set; }
    //    public int AlertInfoId { get; set; }
    //    public int AssetTypeId { get; set; }
    //    public int ZoneId { get; set; }
    //    public int DivisionId { get; set; }
    //    public int? SectionId { get; set; }

    //    // ── DateTime columns (combined from DateFrom+TimeFrom in controller) ──
    //    public DateTime? FromDateTime { get; set; }
    //    public DateTime? ToDateTime { get; set; }
    //    public DateTime? RectificationDateTime { get; set; }

    //    // ── Audit ────────────────────────────────────────────────────
    //    public int CreatedBy { get; set; }
    //    public DateTime CreatedDate { get; set; }
    //    public string DeletedBy { get; set; }   // stored as string in DB
    //    public DateTime? DeletedDate { get; set; }

    //    // ── Remark ───────────────────────────────────────────────────
    //    public string Remark { get; set; }
    //    public string CauseCode { get; set; }   // display name from AlertInfo.Name

    //    // ── Joined / display fields (populated by repository query) ──
    //    public string AssetName { get; set; }
    //    public string AssetType { get; set; }
    //    public string SiteName { get; set; }
    //    public string DivisionName { get; set; }
    //    public string ZoneName { get; set; }
    //    public string SectionName { get; set; }
    //    public string MaintainerName { get; set; }
    //    public string MaintainerDesignation { get; set; }
    //    public string MaintainerMobileNumber { get; set; }

    //    // ── Search-only filters (used in GetLister where clauses) ────
    //    // These are set on SearchCriteria, not stored in DB
    //    public int RoleId { get; set; }
    //    public int UserId { get; set; }
    //    public bool IsAcknowledgement { get; set; }
    //}
    public class PerformanceAlert
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int? AlertInfoId { get; set; }
        public DateTime? FromDateTime { get; set; }
        public DateTime? ToDateTime { get; set; }
        public DateTime? RectificationDateTime { get; set; }

        // C#5 — no => expression bodies, use get { } block
        public string FailureDateFrom
        {
            get
            {
                return FromDateTime.HasValue
                    ? FromDateTime.Value.ToString("dd-MM-yyyy")
                    : string.Empty;
            }
        }

        public string FailureDateTo
        {
            get
            {
                return ToDateTime.HasValue
                    ? ToDateTime.Value.ToString("dd-MM-yyyy")
                    : string.Empty;
            }
        }

        public string FailureTimeFrom
        {
            get
            {
                return FromDateTime.HasValue
                    ? FromDateTime.Value.ToString("HH:mm")
                    : string.Empty;
            }
        }

        public string FailureTimeTo
        {
            get
            {
                return ToDateTime.HasValue
                    ? ToDateTime.Value.ToString("HH:mm")
                    : string.Empty;
            }
        }
        public string RectificationDateTimeFormatted
        {
            get
            {
                return RectificationDateTime.HasValue
                    ? RectificationDateTime.Value.ToString("dd-MM-yyyy")
                    : string.Empty;
            }
        }

        public string Remark { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }
        public string CauseCode { get; set; }
        public string AssetName { get; set; }
        public string SiteName { get; set; }
        public string DivisionName { get; set; }
        public int DivisionId { get; set; }
        public string ZoneName { get; set; }
        public int ZoneId { get; set; }
        public int AssetTypeId { get; set; }
        public string AssetType { get; set; }
        public string SectionName { get; set; }
        public int? SectionId { get; set; }
        public string MaintainerDesignation { get; set; }
        public string MaintainerMobileNumber { get; set; }
        public string MaintainerName { get; set; }
        public int IsManual { get; set; }
        public string GeneratedBy { get; set; }
        public string AlertStatus { get; set; }
    }
}
