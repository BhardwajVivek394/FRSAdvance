namespace Domain
{
    public class DataloggerAssetMapping
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public string RailwayAssetName { get; set; }
        public string RailwayAssetcode { get; set; }
        public string RailwayAssetTypeCode { get; set; }
        //Extra
        public int AssetTypeId { get; set; }
        public string Code { get; set; }
        public string AssetName { get; set; }
    }
}
