using System;
using System.Collections.Generic;

namespace Domain
{
    public class Division : APIResponse
    {
        public int Id { get; set; }
        public int ZoneId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public int? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }
        public int? PDFGenerationType { get; set; }
        public string StoreName { get; set; }
        public string RepresentationCode { get; set; }
        public string Code { get; set; }
        //extra
        public List<int> ZoneIds { get; set; }
        public string ZoneName { get; set; }
        public string DivisionName { get; set; }
        public string DivisionCode { get; set; }
        public string SiteName { get; set; }
        public string AssetType { get; set; }
        public string AssetName { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public string ImagePath { get; set; }
        public string ImageBase64 { get; set; }
        public string ImageExtension { get; set; }
        public int DivisionPDFCount { get; set; }
        public int TotalA10StatusCount { get; set; }
        public int PreviousDayCount { get; set; }
        public int Yesterday30Count { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public Division()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class DivisionLister : APIResponse
    {
        public DivisionLister()
        {
            mDivisions = new List<Division>();
            SearchCriteria = new Division();
            Pager = new Pager();
        }
        public List<Division> mDivisions { get; set; }
        public Division SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}