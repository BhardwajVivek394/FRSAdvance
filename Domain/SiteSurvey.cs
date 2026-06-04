using System.Collections.Generic;

namespace Domain
{
    public class SiteSurvey
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string LocationBoxNo { get; set; }
        public string LocationSide { get; set; }
        public string DistanceFromRR { get; set; }
        public string VC { get; set; }
        public int Cluster { get; set; }
        public string Conductor { get; set; }
        public string TR { get; set; }
        public string TF { get; set; }
        public string PointMachine { get; set; }
        public string Signal { get; set; }
        public string AxleCounter { get; set; }
        public string BusBar { get; set; }
        public string UFSBI { get; set; }
        public string BHMS { get; set; }
        public string FtuBox { get; set; }
        public string InputClusterWise { get; set; }
        public int CreatedBy { get; set; }

        //Extra
        public int VoltageCount { get; set; }
        public int CurrentCount { get; set; }
        public int CS03A { get; set; }
        public int CS10A { get; set; }
        public int CS600A { get; set; }
        public int V10 { get; set; }
        public int V1 { get; set; }
        public int V100 { get; set; }
        public int V150DC { get; set; }
        public int V50DC { get; set; }
        public int V150AC { get; set; }
        public string SiteName { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
    }

    public class SiteSurveyLister
    {
        public SiteSurveyLister()
        {
            SiteSurveys = new List<SiteSurvey>();
            SearchCriteria = new SiteSurvey();
            Pager = new Pager();
            mSiteSurveyClusterStatus = new List<Domain.SiteSurveyClusterStatus>();
        }
        public List<SiteSurvey> SiteSurveys { get; set; }
        public SiteSurvey SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<Domain.SiteSurveyClusterStatus> mSiteSurveyClusterStatus { get; set; }
    }
}
