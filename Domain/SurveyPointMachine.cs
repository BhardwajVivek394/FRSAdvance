namespace Domain
{
    public class SurveyPointMachine
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string Name { get; set; }
        public bool IsHalfPointMachine { get; set; }
        public bool IsThickWave { get; set; }
        public int? ThickWaveTypeId { get; set; }
        public bool IsSeriesOpration { get; set; }
        public int? SeriesOprationTypeId { get; set; }
        public int? MachineA { get; set; }
        public int? MachineB { get; set; }
    }
}
