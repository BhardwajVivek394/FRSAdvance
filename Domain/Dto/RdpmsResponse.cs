using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Domain.Dto
{
    public class RdpmsResponse
    {
        [JsonProperty("Data")]
        public List<AttributeData> Data { get; set; } = new List<AttributeData>();
    }

    public class AttributeData
    {
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AttributeId { get; set; }
        public string TagID { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Key is string "1", "2", etc.
        public Dictionary<string, DataValue> Values { get; set; }
    }

    public class DataValue
    {
        public int Id { get; set; }
        public TimestampInfo Timestamp { get; set; }
        public string Value { get; set; }
        public string DataType { get; set; }
    }

    public class TimestampInfo
    {
        public DateTime TimestampEdgeX { get; set; }
        public DateTime TimestampLocal { get; set; }
        public DateTime TimestampDevice { get; set; }
        public string DataSource { get; set; }
        public DateTime TimestampChannel { get; set; }
        public DateTime TimestampPeriodic { get; set; }
        public DateTime TimestampChange { get; set; }
        public DateTime TimestampEvent { get; set; }
        public string ThresholdMode { get; set; }
    }
}
