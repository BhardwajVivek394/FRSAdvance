namespace Domain
{
    public class FRSWatchList
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }
        public int AssetId { get; set; }
        public int AlertInfoId { get; set; }
        public int CreatedBy { get; set; }
        public bool IsSet { get; set; }
        public string Remark { get; set; }
        public string Description { get; set; }
        public System.DateTime CreatedDate { get; set; }

        //Extra
        public int SiteWiseCount { get; set; }
        public int AlertInstanceCount { get; set; }
        public string SiteName { get; set; }
        public string AssetType { get; set; }
        public string Asset { get; set; }
        public string CauseCode { get; set; }
        public int? IssueTypeId { get; set; }
        public string ClusterName { get; set; }
        public string CardName { get; set; }
        public string ADCName { get; set; }
        public int AttributeId { get; set; }
        public string Attribute { get; set; }
        public string Pin { get; set; }
        public string Reason { get; set; }
        public string MatrialCode { get; set; }
        public int? MaintainerUserId { get; set; }
        public bool? IsResolved { get; set; }
        public string IssueType { get; set; }
    }
}
