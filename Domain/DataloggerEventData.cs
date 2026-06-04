namespace Domain
{
    public class DataloggerEventData
    {
        public int Id { get; set; }
        public int DataloggerFileFormatId { get; set; }
        public string Status { get; set; }
        public System.DateTime TimeStamp { get; set; }


        public int AttributeId { get; set; }
        public string AssetName { get; set; }
        public int SrNo { get; set; }
        public int SiteId { get; set; }
    }
}
