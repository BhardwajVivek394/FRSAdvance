namespace Domain
{
    public class WorksheetStoreMaterial
    {
        public int Id { get; set; }
        public int MaterialId { get; set; }
        public int DivisionId { get; set; }
        public bool IsActive { get; set; }
        //Extra
        public string MaterialName { get; set; }
    }
}
