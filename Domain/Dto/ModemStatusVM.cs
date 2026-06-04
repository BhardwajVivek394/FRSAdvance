using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Dto
{
    public class ModemStatusVM
    {
        public long ModemId { get; set; }
        public string SimNumber { get; set; }
        public int ADCCount { get; set; }
        public string TCPStatus { get; set; }
        public string MQTTStatus { get; set; }
        public string TCPSignalStrength { get; set; }
        public string MQTTSignalStrength { get; set; }
        public string SubscribeTopic { get; set; }
        public string PublishTopic { get; set; }
        public string TCPReceive { get; set; }
        public string TCPSend { get; set; }
    }

}
