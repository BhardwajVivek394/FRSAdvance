namespace Domain
{
    public class SitePointMachine : APIResponse
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string APIPath { get; set; }
        public string UserId { get; set; }
        public string DashboardId { get; set; }
        public string DeviceId { get; set; }
        public string SiteName { get; set; }
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public string ClusterA { get; set; }
        public string ClusterB { get; set; }
    }
}