using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class PerformanceAlertLister
    {
        public PerformanceAlert SearchCriteria { get; set; } = new PerformanceAlert();
        public Pager Pager { get; set; } = new Pager();
        public List<PerformanceAlert> mpfmAlerts { get; set; } = new List<PerformanceAlert>();
    }
}
