namespace Domain
{
    public class SensorHealthLive
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AttId { get; set; }
        public System.DateTime? SetTime { get; set; }
        public decimal? SetValue { get; set; }
        public System.DateTime? ResetTime { get; set; }
        public decimal? ResetValue { get; set; }
        public bool Ack { get; set; }
        public System.DateTime UpdatedTime { get; set; }
    }
}
