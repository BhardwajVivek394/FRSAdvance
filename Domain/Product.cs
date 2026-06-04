using System;

namespace Domain
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string DeviceID { get; set; }
        public string Firmwareversion { get; set; }
        public string SimNumber { get; set; }
        public bool? Status { get; set; }

        public string ConnectorStatus { get; set; }
        public string ClusterName { get; set; }
    }

    public class ProductStatus
    {
        public int Id { get; set; }
        public string DeviceID { get; set; }
        public bool Status { get; set; }
        public DateTime CTime { get; set; }
        public string TimeStamp { get; set; }
    }
}
