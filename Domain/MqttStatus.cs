using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class MqttStatus
    {
        public int A10Id { get; set; }
        public long TsUnix { get; set; }
        public string TimestampString { get; set; }
        public DateTime? TS { get; set; }
        public int Uptime { get; set; }
        public string MQTTStatus { get; set; }   // Active/Inactive
        public string Duration { get; set; }
        public string ModemId { get; set; }
        public DateTime? Timestamp { get; set; }      // parsed timestamp
        public String ResponseTime { get; set; }


    }

    public class MqttStatusCountVM
    {
        public int Active { get; set; }
        public int Inactive { get; set; }
        public int NA { get; set; }
    }


}
