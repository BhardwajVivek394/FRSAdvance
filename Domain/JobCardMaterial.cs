namespace Domain
{
    public class JobCardMaterial
    {
        public int Id { get; set; }
        public int JobCardId { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public bool IsArrived { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
    }
}
