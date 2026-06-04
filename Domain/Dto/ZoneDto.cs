using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Dto
{
    public class ZoneDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class DivisionDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int ZoneId { get; set; }
    }

    public class SiteDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int DivisionId { get; set; }
    }


}
