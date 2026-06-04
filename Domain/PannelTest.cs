namespace Domain
{
    public class PannelTest
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public string FileName { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }

        //Extra
        public string FileBase64 { get; set; }
    }
}
