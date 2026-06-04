namespace Domain
{
    public class CommissioningDocument
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int TypeId { get; set; }
        public string FileName { get; set; }
        public int CreatedBy { get; set; }

        //Extra
        public string FileBase64 { get; set; }
    }
}
