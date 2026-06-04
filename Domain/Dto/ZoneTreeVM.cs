using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Dto
{
    public class ZoneTreeVM
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<DivisionTreeVM> Divisions { get; set; }
    }

    public class DivisionTreeVM
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<SiteTreeVM> Sites { get; set; }
    }

    public class SiteTreeVM
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

}
