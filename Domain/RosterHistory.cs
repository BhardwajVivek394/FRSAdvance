using System;
using System.Collections.Generic;

namespace Domain
{
    public class RosterHistory
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public int SiteId { get; set; }
        public string Status { get; set; }
        public string Remark { get; set; }
        public System.DateTime AssignDate { get; set; }
        public string MaintenanceInput { get; set; }
        public string StringAssignDate { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string SiteName { get; set; }
        public string AssetTypeName { get; set; }
        public string Name { get; set; }
        public int RosterId { get; set; }
        public string DivisionName { get; set; }

        public string ZoneName { get; set; }
        public string GluedImagePath { get; set; }
        public string SignatureImagePath { get; set; }
        public string TrainMomentImagePath { get; set; }
    }

    public class RosterHistoryLister
    {
        public List<RosterHistory> RosterHistories { get; set; } = new List<RosterHistory>();
        public RosterHistory RosterHistory { get; set; } = new RosterHistory();
    }
}
