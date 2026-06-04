namespace Domain
{
    public class FRSAttributeRange
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public int AssetAttributeId { get; set; }
        public int AlertInfoId { get; set; }
        public decimal? MaxSafeValue { get; set; }
        public decimal? MinSafeValue { get; set; }
        public decimal? MinFailValue { get; set; }
        public decimal? AverageValue { get; set; }

        //Extra
        public string AttributeName { get; set; }

    }
}
