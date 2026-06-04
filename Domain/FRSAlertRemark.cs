namespace Domain
{
    public class FRSAlertRemark
    {
        public int Id { get; set; }
        public int? FRSAlertId { get; set; }
        public int AcknowledgementStatusId { get; set; }
        public int? PTAlertInfoId { get; set; }
        public string Remark { get; set; }
        public System.DateTime? RectificationDateTime { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public string MaintainerName { get; set; }
        public string MaintainerDesignation { get; set; }
        public int MaintainerId { get; set; }
        public string PTAlertInfo { get; set; }
    }
}
