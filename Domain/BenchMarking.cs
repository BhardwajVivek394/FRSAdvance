using System.Collections.Generic;

namespace Domain
{
    public class BenchMarking
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AssetAttributeId { get; set; }
        public decimal Value { get; set; }
        public System.DateTime TimeStamp { get; set; }
        public bool IsPrimary { get; set; }

        //Extra
        public string AssetAttribute { get; set; }
    }
    public class BenchMarkingLister : APIResponse
    {
        public BenchMarkingLister()
        {
            BenchMarkings = new List<BenchMarking>();
            SearchCriteria = new BenchMarking();
            Pager = new Pager();
        }
        public List<BenchMarking> BenchMarkings { get; set; }
        public BenchMarking SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
    
}
