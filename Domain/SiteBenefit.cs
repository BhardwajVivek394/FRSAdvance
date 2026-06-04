using System;
using System.Collections.Generic;

namespace Domain
{
    public class SiteBenefit
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int TypeId { get; set; }
        public string Remark { get; set; }
        public int CreatedBy { get; set; }
        public DateTime? RDPMSTimeStamp { get; set; }
        //Extra
        public List<int> FRSAlertIds { get; set; } = new List<int>();
    }
}
