using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class LoginMacAddress
    {
        public int Id { get; set; }
        public string MacAddress { get; set; }
        public bool IsApproved { get; set; }
        public System.DateTime Timestamp { get; set; }

        public string IpAddress { get; set; }
        public string City { get; set; }
        public int? UserId { get; set; }
        public string Email { get; set; }
        public string Source { get; set; }
    }

    public class LoginMacAddressLister:APIResponse
    {
        public LoginMacAddressLister()
        {
            LoginMacAddresses = new List<LoginMacAddress>();
            SearchCriteria = new LoginMacAddress();
            Pager = new Pager();
        }
        public List<LoginMacAddress> LoginMacAddresses { get; set; }
        public LoginMacAddress SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
