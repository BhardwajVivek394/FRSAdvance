namespace Domain
{
    public class AlertMessage
    {
        public string alert_id { get; set; }
        public string device_id { get; set; }
        public string alert_message { get; set; }
        public string alert_date { get; set; }
        public string alert_status { get; set; }
        public string handled_user_id { get; set; }
        public string handled_date { get; set; }
        public string alert_type { get; set; }
        public string company_id { get; set; }
        public string user_alert_text { get; set; }
        public string age { get; set; }
        public string device_name { get; set; }

        public System.DateTime FromDate { get; set; }
        public System.DateTime ToDate { get; set; }
        public int AssetId { get; set; }
    }
}
