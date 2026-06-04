using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class SearchCriteria
    {
        public int UserId { get; set; }
        public int RoleId { get; set; }
        public DateTime TodayDate { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public List<int> SiteIds { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int AssetAttributeId { get; set; }
        public int AssetTypeId { get; set; }
        public int AlertTypeId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status_A { get; set; }
        public string Status_B { get; set; }
        public string ClusterA { get; set; }
        public string ClusterB { get; set; }
        public string AssetName { get; set; }
        public string AI_Cluster { get; set; }
        public string ClusterRG { get; set; }
        public string ClusterDG { get; set; }
        public string ClusterHG { get; set; }
        public bool? IsThickWave { get; set; }
        public string Direction { get; set; }
        public List<string> MultipleClusterA { get; set; }
        public List<string> MultipleClusterB { get; set; }
        public string Month { get; set; }
        public string Year { get; set; }
        public DateTime TimeStamp { get; set; }
        public DataTable DataTable { get; set; } = new DataTable();
        public string Clusters { get; set; }
        public int Week { get; set; }
        public int A10Id { get; set; }
        public string Type { get; set; }
        public int TypeId { get; set; }
        public string Cause { get; set; }
        public string AssetType { get; set; }
        public string AlertType { get; set; }
        public string SearchDate { get; set; }
        public List<int> SrNumbers { get; set; }
        public List<int> IssueTypes { get; set; } = new List<int>();
        public List<string> RelayNames { get; set; } = new List<string>();
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public bool IsAlert { get; set; }
        public string ClusterImageName { get; set; }
        public bool IsCheckAlertType { get; set; }
        public bool IsCheckRailwayAlertNull { get; set; }
        public bool IsCheckResolved { get; set; }
        public bool IsResolved { get; set; }
        public DateTime? ResolvedStartDate { get; set; }
        public DateTime? ResolvedEndDate { get; set; }
        public bool IsAlertInsight { get; set; }

        public List<int> AssetTypeIds { get; set; }
        public List<int> ZoneIds { get; set; }
        public List<int> DivisionIds { get; set; }
        public List<int> AssetIds { get; set; }
    }
}
