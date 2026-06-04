using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class DeviceINI
    {
        public string Section { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public string Address { get; set; }
        public string WebValue { get; set; }
        public int AssetInfoId { get; set; }
        public string Plc { get; set; }
    }
}
