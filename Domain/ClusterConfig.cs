using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class ClusterConfig
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string ClusterName { get; set; }
        public string Param { get; set; }
        public string MqttPath { get; set; }
    }
}
