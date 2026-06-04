namespace Domain
{
    public class CalibrationDetail
    {
        public int Id { get; set; }
        public int AssetInfoId { get; set; }
        public decimal Multiplication { get; set; }
        public decimal? RailwayMultiplication { get; set; }
        public decimal? RealTimeValue { get; set; }
        public decimal? RDPMSMultiplication { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public string UserName { get; set; }

  
    }
}
