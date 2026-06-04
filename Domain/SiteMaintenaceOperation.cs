namespace Domain
{
    public class SiteMaintenaceOperation
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public bool IsIncludeExclude { get; set; }

        public string SiteName { get; set; }
    }
}
