using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class UtilityLog
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string UtilityType { get; set; }
        public string UtilityName { get; set; }
        public System.DateTime TimeStamp { get; set; }
        public string SiteName { get; set; }
        public bool IsLocalSite { get; set; }
        //Extra
        public bool? IsSetupLocalSite { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
    }
}
