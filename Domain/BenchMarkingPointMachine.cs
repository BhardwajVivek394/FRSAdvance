namespace Domain
{
    public class BenchMarkingPointMachine
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public string Direction { get; set; }
        public string ArrayData { get; set; }
        public string TsTime { get; set; }
        public System.DateTime TimeStamp { get; set; }
        public bool IsAutomatic { get; set; }
        public string AssetType { get; set; }
        public bool IsPrimary { get; set; }
        public PointMachineJson PointMachineJson { get; set; } = new PointMachineJson();
    }
}
