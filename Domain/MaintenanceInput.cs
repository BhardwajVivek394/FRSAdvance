using System;
using System.Collections.Generic;

namespace Domain
{
    public class MaintenanceInput
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AttributeId { get; set; }
        public int? IndexScore { get; set; }
        public string MaintenenceInput { get; set; }

        //Extra
        public string SiteName { get; set; }
        public string AssetTypeName { get; set; }
        public string Name { get; set; }
        public int AssetTypeId { get; set; }
        public string ImageBase64 { get; set; }
        public string MaintenanceType { get; set; }
    }
    public class MaintenanceSchedule
    {
        public DateTime ScheduleDate { get; set; }
        public List<MaintenanceInput> maintenanceInputs { get; set; } = new List<MaintenanceInput>();
    }
}
