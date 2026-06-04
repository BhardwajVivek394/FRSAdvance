namespace Domain
{
    public class JobCardUser
    {
        public int Id { get; set; }
        public int JobCardId { get; set; }
        public int UserId { get; set; }

        //Extra
        public string Name { get; set; }
    }
}
