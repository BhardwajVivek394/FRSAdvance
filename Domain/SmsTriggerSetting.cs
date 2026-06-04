namespace Domain
{
    public class SmsTriggerSetting
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AlertId { get; set; }
        public int SmsFrequency { get; set; }
        public int SmsLimit { get; set; }
        public string AlertName { get; set; }
    }
}
