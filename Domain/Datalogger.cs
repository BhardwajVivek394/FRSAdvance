using System;

namespace Domain
{
    public class Datalogger
    {
        public int AssetTypeId { get; set; }
        public int AttributeId { get; set; }
        public string AssetName { get; set; }
        public string CurrentValue { get; set; }
        public string Timestamp { get; set; }
        public DateTime Date { get; set; }
        public int SrNo { get; set; }
        public DateTime CurrentDate { get; set; }
        public string Type { get; set; }
        public string AttributeName { get; set; }
        public int AssetId { get; set; }
    }
}
