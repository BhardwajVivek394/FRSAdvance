using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class COSConfig
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int? Id_MNE { get; set; }
        public int? TYPE { get; set; }
        public bool? VALUE { get; set; }
        public string STRING { get; set; }
        public DateTime? WT_DATE { get; set; }
        public string MNEMONIC { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        //Extra
        public string FromTime { get; set; }
        public string ToTime { get; set; }
        public string SearchText { get; set; }


    }

    public class COSConfigLister : APIResponse
    {
        public COSConfigLister()
        {
            COSConfigs = new List<COSConfig>();
            SearchCriteria = new COSConfig();
            Pager = new Pager();
        }
        public List<COSConfig> COSConfigs { get; set; }
        public COSConfig SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
