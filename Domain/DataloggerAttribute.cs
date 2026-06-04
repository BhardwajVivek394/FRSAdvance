namespace Domain
{
    public class DataloggerAttribute
    {
        public int Id { get; set; }
        public int DataloggerAssetTypeId { get; set; }
        public string Title { get; set; }
        public int CreatedBy { get; set; }
        public int? Type { get; set; }
        public string RepresentationCode { get; set; }
        public string ParameterRepresentationCode { get; set; }

        //Extra
        public string AssetTypeRepresentationCode { get; set; }
        public string DataloggerAssetType { get; set; }
        public int SrNo { get; set; }
        public string ContactType { get; set; }
        public string Value { get; set; }
    }
}
