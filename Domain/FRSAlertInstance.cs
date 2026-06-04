namespace Domain
{
    public class FRSAlertInstance
    {
        public int Id { get; set; }
        public int FRSAlertId { get; set; }
        public string Message { get; set; }
        public System.DateTime TimeStamp { get; set; }
        public string Description { get; set; }
    }
}
