using System.Collections.Generic;

namespace Domain
{
    public class ClusterCategory
    {
        public int Id { get; set; }
        public int AssetTypeId { get; set; }
        public int ClasificationTypeId { get; set; }
        public int StatusId { get; set; }
        public string Name { get; set; }
        public string Remark { get; set; }
        public decimal Percentage { get; set; }
        public string SignatureImage { get; set; }
        public bool IsDivisionPDF { get; set; }
        public int CreatedBy { get; set; }
        public int? PointMachineTypeId { get; set; }

        public int Cluster { get; set; }
        public int SubCluster { get; set; }
        public bool IsUnderObservation { get; set; }

        //Extra Properties
        public string AssetType { get; set; }
        public string ImageBase64 { get; set; }
        public string ImageExtension { get; set; }

    }

    public class ClusterCategoryLister
    {
        public ClusterCategoryLister()
        {
            mClusterCategories = new List<ClusterCategory>();
            SearchCriteria = new ClusterCategory();
            Pager = new Pager();
        }

        public List<ClusterCategory> mClusterCategories { get; set; }
        public ClusterCategory SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
