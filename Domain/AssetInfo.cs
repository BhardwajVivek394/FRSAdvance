using System;

namespace Domain
{
    public class AssetInfo
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public int AssetAttributeId { get; set; }
        public string Channels { get; set; }
        public string Value { get; set; }
        public decimal? Multiplication { get; set; }
        public int? ThresHold { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public decimal? DefaultMultiplication { get; set; }

        //Extra
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }
        public string HttpPostName { get; set; }
        public string AssestTypeName { get; set; }
        public int? GateContectTypeId { get; set; }
        public string AttributeName { get; set; }
        public string ADC_RTime_Value { get; set; }
        public string AssetName { get; set; }
        public string StationCode { get; set; }
        public decimal? RailwayMultiplication { get; set; }
        public decimal? RealTimeValue { get; set; }
        public decimal? RDPMSMultiplication { get; set; }
        public string A10Multiplayer { get; set; }
        public string AttributeAliasName { get; set; }
    }
}