using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class GluedLogData
    {
        public DateTime TimeStamp { get; set; }
        public int MainTrackId { get; set; }
        public string MainTrack { get; set; }
        public string MainTrackValue { get; set; }
        public string FamilyTrack { get; set; }
        public string FamilyTrackValue { get; set; }
        public int SiteId { get; set; }

        //Extra
        public bool IsApp { get; set; }
        public string LokedRecord { get; set; }
    }

    public class GluedLogDataLister : APIResponse
    {
        public GluedLogDataLister()
        {
            GluedLogDatas = new List<GluedLogData>();
            SearchCriteria = new GluedLogData();
            Pager = new Pager();
            Assets = new List<Asset>();
            FamilyTracks = new List<string>();
        }
        public List<GluedLogData> GluedLogDatas { get; set; }
        public GluedLogData SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<Asset> Assets { get; set; }
        public List<string> FamilyTracks { get; set; }
        public string Remark { get; set; }
    }
}
