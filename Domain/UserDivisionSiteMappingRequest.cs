using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    //public class UserDivisionSiteMappingRequest
    //{
    //    public int UserId { get; set; }
    //    public List<int> DivisionIds { get; set; }
    //    public List<int> SiteIds { get; set; }
    //    public int CreatedBy { get; set; }

    //    public UserDivisionSiteMappingRequest()
    //    {
    //        DivisionIds = new List<int>();
    //        SiteIds = new List<int>();
    //    }
    //}
    public class UserDivisionSiteMappingRequest
    {
        public int UserId { get; set; }
        public List<int> DivisionIds { get; set; }
        public List<int> SiteIds { get; set; }

        public UserDivisionSiteMappingRequest()
        {
            DivisionIds = new List<int>();
            SiteIds = new List<int>();
        }
    }
}
