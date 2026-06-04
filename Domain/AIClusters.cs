using System.Collections.Generic;

namespace Domain
{
    public class AIClusters
    {
        public int Id { get; set; }
        public int AssetTypeId { get; set; }
        public string Cluster { get; set; }
        public string Remark { get; set; }
        public string ImagePath { get; set; }
        public int CreatedBy { get; set; }

        //Extra
        public string ImageBase64 { get; set; }
        public string ImageExtension { get; set; }
        public string AssetType { get; set; }
    }

    public class AIClustersLister
    {
        public AIClustersLister()
        {
            AIClusters = new List<AIClusters>();
            SearchCriteria = new AIClusters();
            Pager = new Pager();
        }
        public List<AIClusters> AIClusters { get; set; }
        public AIClusters SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
