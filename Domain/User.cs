using System;
using System.Collections.Generic;

namespace Domain
{
    public class User : APIResponse
    {
        public int Id { get; set; }
        public int UserLevelId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmailAddress { get; set; }
        public string PhoneNumber { get; set; }
        public bool IsActive { get; set; }
        public string Password { get; set; }
        public int RoleId { get; set; }
        public bool IsYardConfig { get; set; }
        public bool IsCalibration { get; set; }
        public DateTime CreatedDate { get; set; }
        public int CreatedBy { get; set; }
        public bool IsMyDaily { get; set; }
        public bool IsSiteKeeping { get; set; }
        public int? UserClassId { get; set; }
        public string VersionNumber { get; set; }
        public int? MaxMobileDevice { get; set; }
        public int? MaxDesktopDevice { get; set; }
        public decimal? CalibrationPercentage { get; set; }
        public bool? IsConfiguration { get; set; }
        //extra
        public bool IsSendOtpToUser { get; set; }
        public int? AnnexureId { get; set; }
        public string RoleName { get; set; }
        public string UserLevel { get; set; }
        public string Title { get; set; }
        public string Token { get; set; }
        public List<SMSLog> mSMSLogs { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public bool IsBlockIp { get; set; }
        public bool IsMacIdApproved { get; set; }
        public string Designation { get; set; }
        public int ZoneId { get; set; }
        public int SiteId { get; set; }
        public int DivisionId { get; set; }
        public string Name { get; set; }
        public int Duration { get; set; }
        public bool? IsTestUser { get; set; }
        public bool? IsChatBotUpload { get; set; }
        public bool? IsAssignAlert { get; set; }
        public bool? IsHighEndAlert { get; set; }
        public bool? SiteKeepingSearch { get; set; }
        public bool? IsAudit { get; set; }
        public int WebDuration { get; set; }
        public string UserName { get; set; }
        public List<UserSite> UserSites { get; set; } = new List<UserSite>();
        public List<int> SiteIds { get; set; } = new List<int>();
        public List<int> SectionIds { get; set; } = new List<int>();
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public List<string> DivisionNames { get; set; }
        public List<string> SectionNames { get; set; }
        public List<string> SiteNames { get; set; }

        // ─────────────────────────────────────────────────────────
        // NEW: Multi-select filter properties for User Management
        // ─────────────────────────────────────────────────────────
        /// <summary>Multiple Division IDs for filtering (overrides single DivisionId when populated)</summary>
        public List<int> DivisionIds { get; set; } = new List<int>();
        /// <summary>Multiple designation titles for filtering, e.g. ["SSE","JE","CSTE"]</summary>
        public List<string> Designations { get; set; } = new List<string>();
        /// <summary>Status filter: "1" = Active, "0" = Inactive. Multiple values = OR logic. Empty = All.</summary>
        public List<string> StatusList { get; set; } = new List<string>();
        public List<int> ZoneIds { get; set; }
        public User()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
            mSMSLogs = new List<SMSLog>();
            ZoneIds = new List<int>();
        }
    }

    public class UserLister : APIResponse
    {
        public UserLister()
        {
            Users = new List<User>();
            SearchCriteria = new User();
            Pager = new Pager();
        }
        public List<User> Users { get; set; }
        public User SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}