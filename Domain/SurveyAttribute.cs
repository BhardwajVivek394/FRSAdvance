namespace Domain
{
    public class SurveyAttribute
    {
        public int Id { get; set; }
        public int AssetTypeId { get; set; }
        public string Title { get; set; }
        public int? CurrentCount { get; set; }
        public int? VoltageCount { get; set; }
        public string CurrentSensor { get; set; }
        public string VoltageSensor { get; set; }
        public int CreatedBy { get; set; }
        public bool DefaultCheck { get; set; }

        //Extra
        public bool IsChecked { get; set; }
    }
}
