using System.Collections.Generic;

namespace Domain
{
    public class DivisionRemark
    {
        public int Id { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }
        public int? AssetId { get; set; }
        public int OperationTypeId { get; set; }
        public string Remark { get; set; }
        public bool IsActive { get; set; }
        public string ImageName { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public int? AlertInfoId { get; set; }
        //Extra
        public string ZoneName { get; set; }
        public string DivisionName { get; set; }
        public string SiteName { get; set; }
        public string AssetType { get; set; }
        public string AssetName { get; set; }
        public string CauseCode { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public string ImageBase64 { get; set; }
    }

    public class DivisionRemarkLister
    {
        public DivisionRemarkLister()
        {
            DivisionRemarks = new List<DivisionRemark>();
            SearchCriteria = new DivisionRemark();
            Pager = new Pager();
        }
        public List<DivisionRemark> DivisionRemarks { get; set; }
        public DivisionRemark SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
