using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class FRSUserDashboardCount
    {
        public int UserId { get; set; }
        public string ZoneName { get; set; }
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int InActiveUsers { get; set; }
        public int TotalSections { get; set; }
        public int TotalStations { get; set; }
        public string Message { get; set; }
        public bool IsSuccess { get; set; }
    }
}
