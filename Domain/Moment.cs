using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Moment
    {
        public DateTime TimeStamp { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public string Aspect { get; set; } //TPR ma ni aavse
        public double Current { get; set; } //Signal ma aavse
        public double Voltage { get; set; }
        public int BlockId { get; set; }
        public int LockedId { get; set; }

        //Extra
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class MomentLister
    {
        public MomentLister()
        {
            mMoments = new List<Moment>();
            SearchCriteria = new Moment();
            Pager = new Pager();
            Assets = new List<Asset>();
        }
        public List<Moment> mMoments { get; set; }
        public Moment SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<Asset> Assets { get; set; }
    }
}
