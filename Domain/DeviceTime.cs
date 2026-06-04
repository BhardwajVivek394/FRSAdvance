using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class DeviceTime
    {
        public int SiteId { get; set; }
        public int UserId { get; set; }
        public bool IsOverride { get; set; }
        public List<string> Roles { get; set; }
    }
}
