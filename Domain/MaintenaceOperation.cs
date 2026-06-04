using System;
using System.Collections.Generic;

namespace Domain
{
    public class MaintenaceOperation
    {
        public int SiteId { get; set; }
        public string SiteName { get; set; }
        public bool SiteStatus { get; set; }
        public DateTime PointLastUpdate { get; set; }
        public int LinkIssueCount { get; set; }
        public int WatchlistCount { get; set; }
        public int WatchlistUserCount { get; set; }
        public DateTime MqttLog { get; set; }
        public DateTime PLClog { get; set; }
        public int PreviousDayAlertCount { get; set; }
        public int Last30DayAlertCount { get; set; }
        public int PreviousDayRebootTotal { get; set; }
        public int PreviousDayAlertAuditCount { get; set; }
        public int PreviousDayDivisionCount { get; set; }
        public int FRSWatchListTrueCount { get; set; }
        public string A10CalibrationLastUpdate { get; set; }
        public string A10BackupLastUpdate { get; set; }

        public string Zone { get; set; }
        public string Division { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int UserId { get; set; }
        public int RoleId { get; set; }

        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public string RouterId { get; set; }
        public string MobileNumber { get; set; }
        public string MacId { get; set; }
        public string MQTTBasePath { get; set; }
        public int JobCardStatus { get; set; }
        public int FRSWatchListCount { get; set; }
    }

    public class MaintenaceOperationLister
    {
        public MaintenaceOperationLister()
        {
            mMaintenaceOperations = new List<MaintenaceOperation>();
            SearchCriteria = new MaintenaceOperation();
            Pager = new Pager();
        }
        public List<MaintenaceOperation> mMaintenaceOperations { get; set; }
        public MaintenaceOperation SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
