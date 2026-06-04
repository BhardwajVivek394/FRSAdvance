using Newtonsoft.Json;
using System;

namespace Domain
{
    public class A10Status
    {
        public string id { get; set; }
        public string name { get; set; }
        public string last_response { get; set; }
        public string status { get; set; }
        public string exceptions { get; set; }
        public string poll_time { get; set; }

        [JsonProperty(PropertyName = "params")]
        public string paramsvalue { get; set; }
        public string type { get; set; }
        public string timestamp { get; set; }
        public DateTime ConvertDate { get; set; }
        public string SiteName { get; set; }
        public Domain.Product mProduct { get; set; } = new Domain.Product();
        public string Reason { get; set; }
        public string MatrialCode { get; set; }
        public int? MaintainerUserId { get; set; }
        public bool? IsResolved { get; set; }
        public A10Status()
        {
            DateTime.TryParse(timestamp, out DateTime cDate);
            ConvertDate = cDate;
        }
    }
}
