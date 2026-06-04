using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class AppKey
    {
        public int Id { get; set; }
        public int Number { get; set; }
        public string AppKey1 { get; set; }
        public string AppValue { get; set; }
        public System.DateTime CreatedDate { get; set; }
    }
}
