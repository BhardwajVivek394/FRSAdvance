namespace Domain
{
    public class TestimonialImage
    {
        public int Id { get; set; }
        public int TestimonialId { get; set; }
        public string ImageName { get; set; }
        public string FileBase64 { get; set; }
        public string FileExtension { get; set; }
        public bool IsRailway { get; set; }
    }
}
