using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Performance
    {
        public DateTime TimeStamp { get; set; }
        public string CsvData { get; set; }
        public int AssetId { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }

        public string SiteName { get; set; }
        public string AssetName { get; set; }
        public string Category { get; set; }

        //Extra
        [JsonIgnore]
        public string Aspect { get; set; }
        [JsonIgnore]
        public string AspectType { get; set; }
        public string Value { get; set; }
        [JsonIgnore]
        public string ProbabilityType { get; set; }
        public List<int> SiteIds { get; set; } = new List<int>();
        public int DivisionId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class PerformanceLister : APIResponse
    {
        public PerformanceLister()
        {
            Performances = new List<Performance>();
            SearchCriteria = new Performance();
            Pager = new Pager();
            AssetAttributes = new List<AssetAttribute>();
            DateList = new List<string>();
        }
        public List<Performance> Performances { get; set; }
        public Performance SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<AssetAttribute> AssetAttributes { get; set; }
        public List<string> DateList { get; set; }
    }
}
