using System;
using System.Collections.Generic;

namespace Domain
{
    public class DivisionReportModel
    {
        public DateTime TimeStamp { get; set; }
        public int SiteId { get; set; }
        public string SiteName { get; set; }
        public int AssetTypeId { get; set; }
        public string AssetTypeName { get; set; }
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public string ThresholdViolations { get; set; }
        public string UnderAIModels { get; set; }
        public string Remark { get; set; }

        public int DivisionId { get; set; }
        public int UpdatedBy { get; set; }
        public DateTime UpdatedDate { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class DivisionReportModelLister
    {
        public DivisionReportModelLister()
        {
            DivisionReportModels = new List<DivisionReportModel>();
            SearchCriteria = new DivisionReportModel();
            Pager = new Pager();
        }
        public List<DivisionReportModel> DivisionReportModels { get; set; }
        public DivisionReportModel SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
