namespace Domain
{
    public class UserSite
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int SiteId { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public bool? IsShowDeviceLastAccessTime { get; set; }
        public bool? IsFRSAlert { get; set; }
    }
}
