using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class ELDEventlog
    {
        public DateTime TimeStamp { get; set; }
        public string Message { get; set; }
        public List<Deadband> mDeadbands { get; set; } = new List<Deadband>();

        //Extra
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public int SiteId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class ELDEventlogLister
    {
        public List<ELDEventlog> ELDEventlogs { get; set; }
        public ELDEventlog SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public ELDEventlogLister()
        {
            ELDEventlogs = new List<ELDEventlog>();
            SearchCriteria = new ELDEventlog();
            Pager = new Pager();
        }
      
    }
}
