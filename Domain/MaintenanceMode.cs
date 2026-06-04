using System;
using System.Collections.Generic;

namespace Domain
{
    public class MaintenanceMode
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public bool IsMaintenceMode { get; set; }
        public System.DateTime ActiveTime { get; set; }
        public System.DateTime? InActiveTime { get; set; }
        public int CreatedBy { get; set; }


        //Extra
        public bool IsCheckMaintenceModeStatus { get; set; }
        public int RoleId { get; set; }
        public int UserId { get; set; }
        public string Site { get; set; }
        public string Division { get; set; }
        public string Zone { get; set; }

        public DateTime FromDate { get; set; }
        public string FromTime { get; set; }
        public DateTime ToDate { get; set; }
        public string ToTime { get; set; }
        public string Asset { get; set; }
        public string AssetType { get; set; }
        public List<int> ZoneIds { get; set; }
        public List<int> DivisionIds { get; set; }
        public List<int> SiteIds { get; set; }
        public List<int> AssetTypeIds { get; set; }
        public List<int> AssetIds { get; set; }
    }

    public class MaintenanceModeLister
    {
        public MaintenanceModeLister()
        {
            mMaintenanceModes = new List<MaintenanceMode>();
            SearchCriteria = new MaintenanceMode();
            Pager = new Pager();
        }
        public List<MaintenanceMode> mMaintenanceModes { get; set; }
        public MaintenanceMode SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
