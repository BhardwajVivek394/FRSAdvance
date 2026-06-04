namespace Domain
{
    public class PointEventLog
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public int TypeId { get; set; }
        public string Name { get; set; }
        public System.DateTime EventTime { get; set; }
        public System.DateTime Timestamp { get; set; }
        public string ResponseCounter { get; set; }
    }
}
