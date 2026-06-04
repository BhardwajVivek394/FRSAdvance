using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class AppAccess
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public System.DateTime StartTime { get; set; }
        public System.DateTime EndTime { get; set; }
        public int Duration { get; set; }

        //Extra
        public int DivisionId { get; set; }
        public int SiteId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
    }

    public class AppAccessLister
    {
        public AppAccessLister()
        {
            mAppAccess = new List<AppAccess>();
            SearchCriteria = new AppAccess();
            Pager = new Pager();
            mUser = new List<User>();
        }
        public List<AppAccess> mAppAccess { get; set; }
        public AppAccess SearchCriteria { get; set; }
        public Pager Pager { get; set; }
        public List<User> mUser { get; set; }
    }
}
