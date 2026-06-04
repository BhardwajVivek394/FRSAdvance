namespace Domain
{
    public class AlertValue
    {
        public int Id { get; set; }
        public int AlertId { get; set; }
        public int AssetId { get; set; }
        public string Value { get; set; }
        public int OperatorId { get; set; }
        public bool IsActive { get; set; }

        public int TotalCount { get; set; }
        public string AlertName { get; set; }
    }
}
