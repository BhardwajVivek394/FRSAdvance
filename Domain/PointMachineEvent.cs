namespace Domain
{
    public class PointMachineEvent
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public string Direction { get; set; }
        public int VoltageCount { get; set; }
        public int CurrentCount { get; set; }
        public int TimeOfOpration { get; set; }
        public System.DateTime UpdateTime { get; set; }
        public System.DateTime CaptureTime { get; set; }
        public System.DateTime OperationTime { get; set; }
        //Extra
        public string AssetName { get; set; }
        public string Type { get; set; }
    }
}
