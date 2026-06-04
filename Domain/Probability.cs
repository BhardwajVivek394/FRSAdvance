using System;
using System.Collections.Generic;

namespace Domain
{
    public class Probability
    {
        public int UserId { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }
        public int AssetId { get; set; }
        public int DivisionId { get; set; }
        public string SiteName { get; set; }
        public string AssetTypeName { get; set; }
        public string AssetName { get; set; }
        public DateTime TimeStamp { get; set; }
        public string Category { get; set; }
        public string CategoryAlias { get; set; }
        public bool? Validity { get; set; }
        public string ValidityRemark { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<ProbabilityRemark> ProbabilityRemarks { get; set; } = new List<ProbabilityRemark>();
        public int RoleId { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }

        public Probability()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class ProbabilityLister
    {
        public ProbabilityLister()
        {
            Probability = new List<Probability>();
            SearchCriteria = new Probability();
            Pager = new Pager();
            AssetTypes = new List<AssetType>();
            Assets = new List<Asset>();
        }
        public List<Probability> Probability { get; set; }
        public Probability SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<AssetType> AssetTypes { get; set; }
        public List<Asset> Assets { get; set; }
    }
}
