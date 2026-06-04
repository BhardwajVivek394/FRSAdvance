using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class TrainRunningMoment
    {
        public DateTime TimeStamp { get; set; }
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public double IfMa { get; set; }
        public double IrMa { get; set; }
        public double Leakage { get; set; }
        public double Vr { get; set; }
        public double Tpr { get; set; }
        public int BlockId { get; set; }
        public int SiteId { get; set; }

        //Extra
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string LokedRecord { get; set; }
    }

    public class TrainRunningMomentLister
    {
        public TrainRunningMomentLister()
        {
            TrainRunningMoments = new List<TrainRunningMoment>();
            Assets = new List<Asset>();
            SearchCriteria = new TrainRunningMoment();
            Pager = new Pager();
            Blocks = new List<int>();
        }
        public List<TrainRunningMoment> TrainRunningMoments { get; set; }
        public TrainRunningMoment SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<Asset> Assets { get; set; }
        public List<int> Blocks { get; set; }
        public string Remark { get; set; }

    }
}
