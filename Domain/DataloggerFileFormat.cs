namespace Domain
{
    public class DataloggerFileFormat
    {
        public int Id { get; set; }
        public int SrNo { get; set; }
        public int SiteId { get; set; }
        public string RelayName { get; set; }
        public string RelayType { get; set; }
        public string SpareContact { get; set; }
        public string ContactType { get; set; }
        public int CreatedBy { get; set; }
        public int LastModifiedBy { get; set; }
        public System.DateTime LastModifiedDate { get; set; }
        //Extra
        public string JsonString { get; set; }

    }

}
