namespace Domain
{
    public class AlertInfoActivation
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public int AlertInfoId { get; set; }
        public bool IsActive { get; set; }

        public string CauseCode { get; set; }
        public string Asset { get; set; }
        public string AssetType { get; set; }
        public string Site { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public bool UpdateStatus { get; set; }
    }
}
