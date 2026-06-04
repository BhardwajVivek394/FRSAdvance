using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class JsonData
    {
        public int Id { get; set; }
        public string JsonValue { get; set; }
        public System.DateTime CreateDate { get; set; }
        public int? SiteId { get; set; }
    }
}
