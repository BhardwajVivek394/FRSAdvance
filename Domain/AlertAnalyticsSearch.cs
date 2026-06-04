using System;

namespace Domain
{
    public class AlertAnalyticsSearch
    {
        public int SiteId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int AssetTypeId { get; set; }
        public int AssetId { get; set; }
        public int AlertId { get; set; }
    }
}
