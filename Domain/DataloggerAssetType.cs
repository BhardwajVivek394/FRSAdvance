using System.Collections.Generic;

namespace Domain
{
    public class DataloggerAssetType
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public string RepresentationCode { get; set; }

        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public DataloggerAssetType()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class DataloggerAssetTypeLister
    {
        public DataloggerAssetTypeLister()
        {
            mDataloggerAssetTypes = new List<DataloggerAssetType>();
            SearchCriteria = new DataloggerAssetType();
            Pager = new Pager();
        }
        public List<DataloggerAssetType> mDataloggerAssetTypes { get; set; }
        public DataloggerAssetType SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
