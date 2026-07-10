namespace Domain
{
    public class FRSAttributeRangeHistory
    {
        public int Id { get; set; }
        public int FRSAttributeRangeId { get; set; }
        public decimal AvgValueHistory { get; set; }
        public string Json { get; set; }
        public System.DateTime StartTime { get; set; }
        public System.DateTime EndTime { get; set; }
        public System.DateTime Timestamp { get; set; }

        //Extra
        public string Attribute { get; set; }
        public string AttributeTitle { get; set; }
    }
}
