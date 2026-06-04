using System;
using System.Collections.Generic;

namespace Domain
{
    public class Stock
    {
        public int Id { get; set; }
        public int TypeId { get; set; }
        public int ProjectId { get; set; }
        public string TokenNumber { get; set; }
        public int CreatedBy { get; set; }
        public int DivisionId { get; set; }
        public DateTime CreatedDate { get; set; }
        public string OpratorName { get; set; }
        public string OpratorPhoneNumber { get; set; }
        public int? DispatchType { get; set; }
        public string DispatchTypeText { get; set; }
        public string AttendentName { get; set; }
        public int? AuditedBy { get; set; }
        public int? DispatchedBy { get; set; }
        public System.DateTime? AuditedDate { get; set; }
        public System.DateTime? DispatchedDate { get; set; }
        public bool IsDispatch { get; set; }

        //Extra

        public bool IsAudit { get; set; }
        public int AuditedCount { get; set; }
        public int ZoneId { get; set; }
        public int MaterialCount { get; set; }
        public int DispatchedCount { get; set; }
        public string CreatedName { get; set; }
        public string Project { get; set; }
        public string AuditedName { get; set; }
        public string DispatchedName { get; set; }
        public List<Domain.StockSite> StockSites { get; set; } = new List<Domain.StockSite>();
        public List<Domain.StockWorksheetMaterial> StockWorksheetMaterials { get; set; } = new List<Domain.StockWorksheetMaterial>();
    }

    public class StockLister
    {
        public StockLister()
        {
            mStocks = new List<Stock>();
            SearchCriteria = new Stock();
            Pager = new Pager();
        }
        public List<Stock> mStocks { get; set; }
        public Stock SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
