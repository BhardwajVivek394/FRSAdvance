using System;

namespace Domain
{
    public class DynamoTable
    {
        public int asset_attribute_id { get; set; }
        public DateTime timestamp { get; set; }
        public string asset_name { get; set; }
        public string asset_attribute_title { get; set; }
        public string channels { get; set; }
        public string uuid { get; set; }
        public double asset_attribute_value { get; set; }
        public string asset_type_name { get; set; }
        public int site_id { get; set; }
        public int asset_id { get; set; }
        public string site_name { get; set; }
        public DateTime change_timestamp { get; set; }
        public int? ThresHold { get; set; }


    }
}
