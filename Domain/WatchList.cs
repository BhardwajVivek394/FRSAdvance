using System;
using System.Collections.Generic;

namespace Domain
{
    public class WatchList
    {
        public int Id { get; set; }
        public int SerialNumber { get; set; }
        public int SiteId { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public int AssetTypeId { get; set; }
        public int AttributeId { get; set; }
        public int AssetId { get; set; }
        public string SiteName { get; set; }
        public string AssetTypeName { get; set; }
        public string ClusterName { get; set; }
        public string AssetName { get; set; }
        public string AttributeName { get; set; }
        public DateTime CreatedDate { get; set; }
        public string AttributeValue { get; set; }
        public string CardName { get; set; }
        public string ADCName { get; set; }
        public string Pin { get; set; }
        public string Status { get; set; }
        public string Issue { get; set; }
        public string Remark { get; set; }
        public int UserId { get; set; }
        public int RoleId { get; set; }
        public List<CardLine> CardLines { get; set; }
        public string Category { get; set; }
        public string ValidityRemark { get; set; }
        public bool? Validity { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int CreatedBy { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public string PointMachineInstance { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public int CreationType { get; set; }
        public string Cause { get; set; }
        public string GeneratedFrom { get; set; }

        //Extra
        public string Reason { get; set; }
        public string MatrialCode { get; set; }
        public int? MaintainerUserId { get; set; }
        public bool? IsResolved { get; set; }
        public string IssueType { get; set; }
        public WatchList()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }

    }

    public class WatchListLister
    {
        public WatchListLister()
        {
            WatchLists = new List<WatchList>();
            SearchCriteria = new WatchList();
            Pager = new Pager();
            AssetTypes = new List<AssetType>();
        }
        public List<WatchList> WatchLists { get; set; }
        public WatchList SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<AssetType> AssetTypes { get; set; }

    }
}
