using System;
using System.Collections.Generic;

namespace Domain
{
    public class SMSLog
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AlertId { get; set; }
        public string MobileNumber { get; set; }
        public string Message { get; set; }
        public DateTime TimeStamp { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public bool? IsSmsLogActive { get; set; }
        public string AlertType { get; set; }
        public bool? IsExclude { get; set; }
        //Extra
        public string SiteName { get; set; }
        public string StationCode { get; set; }
        public int RoleId { get; set; }
        public int UserId { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public string IsActive { get; set; } = "1";
        public int AssetTypeId { get; set; }
        public string AssetName { get; set; }
        public string AssetTypeName { get; set; }
        public string Remark { get; set; }
        public int DivisionId { get; set; }
        public List<int> SiteIds { get; set; }
        public List<int> Ids { get; set; }
        public string AlertName { get; set; }
        public List<string> AlertNames { get; set; }
        public bool? Validity { get; set; }
        public int AlertTypeId { get; set; }
        public List<SMSLogRemark> mSMSLogRemarks { get; set; } = new List<SMSLogRemark>();
        public bool isTenDays { get; set; }
        public int TenDaysCount { get; set; }
        public SMSLog()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class SMSLogLister : APIResponse
    {
        public SMSLogLister()
        {
            mSMSLogs = new List<SMSLog>();
            SearchCriteria = new SMSLog();
            Pager = new Pager();
            AssetTypes = new List<AssetType>();
            Assets = new List<Asset>();
        }
        public List<SMSLog> mSMSLogs { get; set; }
        public SMSLog SearchCriteria { get; set; }
        public List<AssetType> AssetTypes { get; set; }
        public List<Asset> Assets { get; set; }
        public Pager Pager { get; set; }
    }
}