using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Dto
{
    public class DashboardPageVM
    {
        // Sidebar tree
        public List<ZoneTreeVM> Zones { get; set; }

        // Header counts
        public int TotalZones { get; set; }
        public int TotalDivisions { get; set; }
        public int TotalStations { get; set; }

        // Modem / A10 counts (from SP)
        public int TotalModems { get; set; }
        public int TotalVersion1 { get; set; }
        public int TotalVersion2 { get; set; }

        public int V1MqttOnline { get; set; }
        public int V2MqttOnline { get; set; }

        public int TotalA10Count { get; set; }

        //public int A10V1TCPOnline { get; set; }
        //public int A10V2TCPOnline { get; set; }
        //public int A10V1TCPOffline { get; set; }
        //public int A10V2TCPOffline { get; set; }
        public int TCPA10Count { get; set; }
        public int MQTTA10Count { get; set; }
        public int TCPA10OnlineCount { get; set; }
        public int MQTTA10OnlineCount { get; set; }
        public int ModemErrorCount { get; set; }
        public int TCPErrorCount { get; set; }

        //public int TotalErrors => A10V1TCPOffline + A10V2TCPOffline;
    }
}
