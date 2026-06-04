using System;
using System.Collections.Generic;

namespace Domain
{
    public class SiteAttributeData : APIResponse
    {
        public int Id { get; set; }
        public DateTime TimeStamp { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AttributeId { get; set; }
        public string AttributeData { get; set; }
        public int? CreatedBy { get; set; }

        //extra
        public string SiteName { get; set; }
        public Dictionary<int, string> SiteAttributes { get; set; } = new Dictionary<int, string>();
    }
}