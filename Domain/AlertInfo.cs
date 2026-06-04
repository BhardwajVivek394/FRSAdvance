namespace Domain
{
    public class AlertInfo
    {
        public int Id { get; set; }
        public int AssetTypeId { get; set; }
        public string Name { get; set; }
        public int AlertType { get; set; }
        public bool IsActive { get; set; }

        //Extra
        public int? AssetAttributeId { get; set; }
    }
}
