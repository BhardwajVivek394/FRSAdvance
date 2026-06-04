using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class VibrationConfig
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int AssetId { get; set; }
        public int VibrationTypeId { get; set; }
        public string Address { get; set; }
        public string Param { get; set; }
        public string AssetName { get; set; }
        public string VibrationType { get; set; }
    }
}
