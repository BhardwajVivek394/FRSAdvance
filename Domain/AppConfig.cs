namespace Domain
{
    public class AppConfig
    {
        public int Id { get; set; }
        public int? ZoneId { get; set; }
        public int? UserClassId { get; set; }
        public int? AssetTypeId { get; set; }
        public int? AlertInfoId { get; set; }
        public string GroupName { get; set; }
        public string AppKey { get; set; }
        public string AppValue { get; set; }
        public int? Unit { get; set; }
    }
}
