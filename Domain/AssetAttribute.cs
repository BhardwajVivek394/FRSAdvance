using System;
using System.Collections.Generic;

namespace Domain
{
    public class AssetAttribute : APIResponse
    {
        public int Id { get; set; }
        public int AssetTypeId { get; set; }
        public string Title { get; set; }
        public int CreatedBy { get; set; }
        public bool? IsDigital { get; set; }

        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public decimal? MinThreshold { get; set; }
        public decimal? MaxThreshold { get; set; }
        public decimal? ZeroOffset { get; set; }
        public int? Sequence { get; set; }
        public decimal? DefaultMultiplication { get; set; }
        public decimal? PannelTestValue { get; set; }
        public string AliasName { get; set; }
        public bool? IsDerived { get; set; }
        public int? ValueType { get; set; }
        public int? SensorType { get; set; }
        public int? Type { get; set; }
        public string RepresentationCode { get; set; }
        public decimal? Mode { get; set; }
        public decimal? ThresholdValue { get; set; }
        public decimal? Absolute { get; set; }
        //extra
        public string AssetTypeRepresentationCode { get; set; }
        public string ParameterRepresentationCode { get; set; }
        public string AssetInfoChannel { get; set; }
        public string AssetInfoValue { get; set; }
        public decimal? Multiplication { get; set; }
        public decimal? ThresHold { get; set; }
        public string AttributeData { get; set; }
        public string AssetTypeName { get; set; }
        public string AssetName { get; set; }
        public int? GateContectTypeId { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public List<string> Data { get; set; }
        public List<string> DataArray { get; set; }
        public string MultiplicationValue { get; set; }
        public string HttpPostName { get; set; }
        public List<decimal> Datas { get; set; } = new List<decimal>();
        public List<string> Dates { get; set; } = new List<string>();
        public DateTime TimeStamp { get; set; }
        public AssetAttribute()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
            Data = new List<string>();
            DataArray = new List<string>();
        }
    }

    public class AssetAttributeLister : APIResponse
    {
        public AssetAttributeLister()
        {
            AssetAttributes = new List<AssetAttribute>();
            SearchCriteria = new AssetAttribute();
            Pager = new Pager();
        }
        public List<AssetAttribute> AssetAttributes { get; set; }
        public AssetAttribute SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}