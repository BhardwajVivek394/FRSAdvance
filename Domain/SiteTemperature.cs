namespace Domain
{
    public class SiteTemperature
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string Latitude { get; set; }
        public string Longitude { get; set; }
        public string PickTime { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public int FetchTemperature { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
    }
}
