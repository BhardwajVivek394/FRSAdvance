using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class LoginHistory
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string IpAddress { get; set; }
        public string City { get; set; }
        public System.DateTime? LogInTime { get; set; }
        public System.DateTime? LogOutTime { get; set; }
        public string Name { get; set; }
        public string EmailAddress { get; set; }
        public System.DateTime? TimeStamp { get; set; }
        public System.DateTime? FormDate { get; set; }
        public System.DateTime? ToDate { get; set; }
        public int SiteId { get; set; }
        public int Duration { get; set; }
    }

    public class LoginHistoryLister
    {
        public LoginHistoryLister()
        {
            LoginHistorys = new List<LoginHistory>();
            SearchCriteria = new LoginHistory();
            Pager = new Pager();
        }
        public List<LoginHistory> LoginHistorys { get; set; }
        public LoginHistory SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
