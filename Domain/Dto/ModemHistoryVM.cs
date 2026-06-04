using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Dto
{
    public class ModemHistoryVM
    {
        public string ModemId { get; set; }
        public int TypeId { get; set; }
        public string SignalStrength { get; set; }
        public DateTime TimeStamp { get; set; }
        public string UtilityTime { get; set; }
        public string SignalPercent { get; set; }

        // Computed property
        //public bool IsOnline => !string.IsNullOrEmpty(SignalStrength) && SignalStrength != "0";
        public bool Status { get; set; }  // Add this property

        // Updated computed property - now checks Status column
        public bool IsOnline => Status;
    }

    public class ModemHistory
    {
        public string DeviceId { get; set; }
        public DateTime TimeStamp { get; set; }
        public DateTime DeviceTime { get; set; }
        public int SiteId { get; set; }
        public int TypeId { get; set; }
        public decimal? SignalStrength { get; set; }
        public int? Uptime { get; set; }
        public int? ResponseTime { get; set; }
    }
}
