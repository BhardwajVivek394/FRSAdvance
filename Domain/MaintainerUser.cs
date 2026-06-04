using System.Collections.Generic;

namespace Domain
{
    public class MaintainerUser
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneNumber { get; set; }
        public int CreatedBy { get; set; }
        //Extra
        public string Name { get; set; }
    }

    public class MaintainerUserLister
    {
        public MaintainerUserLister()
        {
            MaintainerUsers = new List<MaintainerUser>();
            SearchCriteria = new MaintainerUser();
            Pager = new Pager();
        }
        public List<MaintainerUser> MaintainerUsers { get; set; }
        public MaintainerUser SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
