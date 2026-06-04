namespace Domain
{
    public class DataloggerInfo
    {
        public int Id { get; set; }
        public int DataloggerAssetId { get; set; }
        public int DataloggerAttributeId { get; set; }
        public int SrNo { get; set; }
        public string ContactType { get; set; }
        public int CreatedBy { get; set; }

        //Extra
        public string DataloggerAttribute { get; set; }
    }
}
