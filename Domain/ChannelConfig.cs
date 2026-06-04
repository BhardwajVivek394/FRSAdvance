using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class ChannelConfig
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public string Channel { get; set; }
        public string MacId { get; set; }


        public bool IsDown { get; set; }
        public string TimeStamp { get; set; }
    }
}
