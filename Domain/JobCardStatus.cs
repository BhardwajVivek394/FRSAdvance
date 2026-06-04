namespace Domain
{
    public class JobCardStatus
    {
        public int Id { get; set; }
        public int JobCardId { get; set; }
        public int TypeId { get; set; }
        public string Description { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
    }
}
