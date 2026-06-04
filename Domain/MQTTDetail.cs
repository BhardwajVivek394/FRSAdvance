using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class MQTTDetail
    {
        public int Id { get; set; }

        [Required]
        public string MQTTUri { get; set; }
        [Required]
        public string MQTTUsername { get; set; }
        [Required]
        public string MQTTPassword { get; set; }
        [Required]
        public int MQTTPort { get; set; }

        public string Type { get; set; }
    }

    public class MQTTDetailList
    {
        public MQTTDetail mQTTDetailWeb { get; set; } = new MQTTDetail();
        public MQTTDetail mQTTDetailUtility { get; set; } = new MQTTDetail();
    }
}
