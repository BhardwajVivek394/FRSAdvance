using System;

namespace Domain
{
    public class GlobalConfig
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public int AssetAttributeId { get; set; }
        public Nullable<decimal> Value { get; set; }
        public string AttributeName { get; set; }
    }
}
