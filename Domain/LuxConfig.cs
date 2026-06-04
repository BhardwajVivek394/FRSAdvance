namespace Domain
{
    public class LuxConfig
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public string Address { get; set; }
        public string Param { get; set; }
        public bool IsRed { get; set; }
        public bool IsGreen { get; set; }
        public bool IsYellow { get; set; }
        public bool IsDoubleYellow { get; set; }
        public string AssetName { get; set; }
        public System.DateTime FromDate { get; set; }
    }
}
