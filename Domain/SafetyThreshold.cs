using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class SafetyThreshold
    {
        public double? MaxSafe { get; set; }
        public double? MinSafe { get; set; }
        public double? MinFail { get; set; }
    }
}
