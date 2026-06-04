namespace Domain
{
    public class UserSection
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int SectionId { get; set; }

        // Navigation / display helpers
        public string SectionName { get; set; }
        public string DivisionName { get; set; }
        public int DivisionId { get; set; }
    }
}
