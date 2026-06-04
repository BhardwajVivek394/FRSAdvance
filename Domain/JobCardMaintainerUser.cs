namespace Domain
{
    public class JobCardMaintainerUser
    {
        public int Id { get; set; }
        public int JobCardId { get; set; }
        public int MaintainerUserId { get; set; }

        //Extra
        public string Name { get; set; }
    }
}
