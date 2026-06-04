using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Summary
    {
        public string FileExtension { get; set; }
        public string ImageBase64 { get; set; }
        public string Date { get; set; }
        public string SiteName { get; set; }
    }
}
