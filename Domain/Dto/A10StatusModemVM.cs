using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Dto
{
    public class A10StatusModemVM
    {
        public int TypeId { get; set; }
        public int SiteId { get; set; }
        public int A10Id { get; set; }
        public bool Status { get; set; }
        public DateTime TimeStamp { get; set; }
        public DateTime UtilityTime { get; set; }
        public string Uptime { get; set; }
        public string PollTime { get; set; }
        public string ResponseTime { get; set; }
    }
}
