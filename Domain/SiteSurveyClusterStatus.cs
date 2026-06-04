namespace Domain
{
    public class SiteSurveyClusterStatus
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int Cluster { get; set; }
        public int StatusTypeId { get; set; }
        public bool IsChecked { get; set; }
        public int? TerminationTrackCount { get; set; }
        public int? TerminationPointCount { get; set; }
        public int? TerminationSignalCount { get; set; }

        //Extra
        public string Name { get; set; }
        public string Text { get; set; }
    }
}
