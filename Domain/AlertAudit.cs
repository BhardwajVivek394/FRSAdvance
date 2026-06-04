namespace Domain
{
    public class AlertAudit
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AlertId { get; set; }
        public string Remark { get; set; }
        public string PortfolioRemark { get; set; }
        public bool IsAlert { get; set; }
        public bool IsOpenClose { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public string MaintainerRemark { get; set; }
        public bool? IsMaintainerAlert { get; set; }

        //Extra
        public int AssetTypeId { get; set; }
        public string Site { get; set; }
        public string AssetType { get; set; }
        public string Asset { get; set; }
        public string Alert { get; set; }
        public string CreatedName { get; set; }
        public string LastModifiedName { get; set; }
    }
}
