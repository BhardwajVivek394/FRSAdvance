using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class AdvancedSIPView
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string SipView { get; set; }
        public System.DateTime CreatedDate { get; set; }
    }
}
