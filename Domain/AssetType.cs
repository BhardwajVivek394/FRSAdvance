using System;
using System.Collections.Generic;

namespace Domain
{
    public class AssetType : APIResponse
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public int? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }
        public bool? IsListView { get; set; }
        public bool? IsBasicAlert { get; set; }
        public string RepresentationCode { get; set; }
        //extra

        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public int TotalCount { get; set; }
        public decimal? X { get; set; }
        public decimal? Y { get; set; }
        public decimal? Z { get; set; }
        public AssetType()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class AssetTypeLister : APIResponse
    {
        public AssetTypeLister()
        {
            mAssetTypes = new List<AssetType>();
            SearchCriteria = new AssetType();
            Pager = new Pager();
            SiteIds = new List<int>();
        }
        public List<AssetType> mAssetTypes { get; set; }
        public List<int> SiteIds { get; set; }
        public AssetType SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}