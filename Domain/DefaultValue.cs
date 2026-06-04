namespace Domain
{
    public class DefaultValue
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int? AttributeId { get; set; }
        public int? DataLoggerAttributeId { get; set; }
        public decimal Value { get; set; }
        public int ConditionTypeId { get; set; }
        //
        public string CauseCode { get; set; }
        public int AssetTypeId { get; set; }
        public int AlertTypeId { get; set; }
    }
}
