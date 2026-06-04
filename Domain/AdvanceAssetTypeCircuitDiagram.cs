namespace Domain
{
    public class AdvanceAssetTypeCircuitDiagram
    {
        public int Id { get; set; }
        public int AssetTypeId { get; set; }
        public string CircuitDiagram { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
    }
}
