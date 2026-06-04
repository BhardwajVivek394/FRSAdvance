namespace Domain
{
    public class StockWorksheetMaterial
    {
        public int Id { get; set; }
        public int MaterialId { get; set; }
        public int StockId { get; set; }
        public int Quantity { get; set; }
        public int? AuditedBy { get; set; }
        public int? DispatchQuantity { get; set; }
        public string DispatchRemark { get; set; }
        public string DispatchLocationFrom { get; set; }
        public string DispatchLocationTo { get; set; }
        public int? DispatchedBy { get; set; }
        public System.DateTime? DispatchedDate { get; set; }
        //Extra
        public bool IsAudit { get; set; }
        public string MaterialName { get; set; }
    }
}
