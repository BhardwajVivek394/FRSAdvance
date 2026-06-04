namespace Domain
{
    public class TrainMoment
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int LineTypeId { get; set; }
        public string EntryTime { get; set; }
        public string ExitTime { get; set; }
        public int CreatedBy { get; set; }
        public bool IsVerify { get; set; }
        public string TrainNumber { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public bool IsVerifyNextStation { get; set; }
        public string UniqueId { get; set; }
        //Extra
        public string StationCode { get; set; }
    }
}
