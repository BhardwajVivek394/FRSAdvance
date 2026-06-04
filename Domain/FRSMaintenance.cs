using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class FRSMaintenance
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public string AssetType { get; set; }
        public int SiteId { get; set; }
        public string StationCode { get; set; }
        public string SiteName { get; set; }
        public int DivisionId { get; set; }
        public string DivisionName { get; set; }
        public int ZoneId { get; set; }
        public string ZoneName { get; set; }
        public DateTime FromTime { get; set; }
        public DateTime ToTime { get; set; }
        public string Remark { get; set; }
        public string CreatedByName { get; set; }
        public string CreatedByRole { get; set; }
        public DateTime? ClearedAt { get; set; }   // null = still active
    }

    public class FRSMaintenanceLister
    {
        public List<FRSMaintenance> Items { get; set; } = new List<FRSMaintenance>();
        public int ActiveCount { get; set; }
    }

}
