namespace Domain
{
    public class Alert
    {
        public int Id { get; set; }
        public int AssetTypeId { get; set; }
        public string AlertName { get; set; }
        public string AlertType { get; set; }
        //extra
        public bool IsActive { get; set; }
        public string AlertValue { get; set; }
        public string AlertValueB { get; set; }
        public string MaxValueA { get; set; }
        public string MaxValueB { get; set; }
        public string AlertThickWaveValue { get; set; }
        public int OperatorId { get; set; }
    }
}