using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class PointCsv
    {
        public string timestamp { get; set; }
        public string name { get; set; }
        public string value { get; set; }
        public string Status { get; set; }
        public string Average { get; set; }
        public string Max { get; set; }
        public string Time { get; set; }
        public int? Scale { get; set; }
        public DateTime Date
        {
            get
            {
                return Convert.ToDateTime(timestamp);
            }
            set
            {
                value = Convert.ToDateTime(timestamp);
            }
        }

        public bool IsUSed { get; set; }

        //Extra
        public int AssetId { get; set; }
        public string AssetName { get; set; }
        public string SiteName { get; set; }
        public int? ThickWaveTypeId { get; set; }
        public bool? IsHalfPointMachine { get; set; }
        public bool? IsSeriesOpration { get; set; }
    }
}
