using System;
using System.Collections.Generic;

namespace Domain
{
    public class TCPLink
    {
        public int SiteId { get; set; }
        public int ServerId { get; set; }
        public string ClientValue { get; set; }
        public string Port { get; set; }
        public string Status { get; set; }
        public DateTime TimeStamp { get; set; }

        //Extra
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public string Zone { get; set; }
        public string Division { get; set; }
        public string Site { get; set; }
    }

    public class TCPLinkLister
    {
        public TCPLinkLister()
        {
            TCPLinks = new List<TCPLink>();
            SearchCriteria = new TCPLink();
            Pager = new Pager();
        }
        public List<TCPLink> TCPLinks { get; set; }
        public TCPLink SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
