using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class SiteKeeping
    {
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public string Type { get; set; }
        public string ImageBase64 { get; set; }
    }
}
