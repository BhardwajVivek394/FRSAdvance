using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Dto
{
    public class ModemSummaryResponse
    {
        public int StationCount { get; set; }
        public int ModemCount { get; set; }
        public int ModemV1Count { get; set; }
        public int ModemV2Count { get; set; }
        public int ModemV1OnlineCount { get; set; }
        public int ModemV2OnlineCount { get; set; }
        public int A10Count { get; set; }
        public int TCPA10Count { get; set; }
        public int MQTTA10Count { get; set; }
        public int TCPA10OnlineCount { get; set; }
        public int MQTTA10OnlineCount { get; set; }
        public int ModemErrorCount { get; set; }
        public int TCPErrorCount { get; set; }
    }

    public class ModemSummaryData
    {
        public int TotalModems { get; set; }
        public int TotalVersion1 { get; set; }
        public int TotalVersion2 { get; set; }
        public int V1MqttOnline { get; set; }
        public int V2MqttOnline { get; set; }
        public int TotalA10Count { get; set; }
        public int TCPA10Count { get; set; }
        public int MQTTA10Count { get; set; }
        public int TCPA10OnlineCount { get; set; }
        public int MQTTA10OnlineCount { get; set; }
        public int TotalStations { get; set; }
        public int ModemErrorCount { get; set; }
        public int TCPErrorCount { get; set; }
    }
}
