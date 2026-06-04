namespace Domain
{
    public class AICluster
    {
        public int AssetTypeId { get; set; }
        public int AssetId { get; set; }
        public string SiteName { get; set; }
        public string AssetName { get; set; }
        public string Cluster { get; set; }
        public string Value { get; set; }
        public string Week { get; set; }
    }

    public class AIClusterCount
    {
        public string Cluster { get; set; }
        public int Count { get; set; }
        public decimal Percentage { get; set; }
        public int Count_R { get; set; }
        public decimal Percentage_R { get; set; }
        public int Count_G { get; set; }
        public decimal Percentage_G { get; set; }
        public int Count_H { get; set; }
        public decimal Percentage_H { get; set; }
        public string Week { get; set; }
    }
}
