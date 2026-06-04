using System.Collections.Generic;

namespace Domain
{
    public class SiteNumber : APIResponse
    {
        public int Id { get; set; }
        public int SiteId { get; set; }
        public int UserClassId { get; set; }
        public string MobileNumber { get; set; }

        //extra
        public List<UserClass> UserClasses { get; set; }
        public SiteNumber()
        {
            UserClasses = new List<UserClass>();
        }
    }

    public class SiteNumberLister : APIResponse
    {
        public SiteNumberLister()
        {
            SiteNumbers = new List<SiteNumber>();
            SearchCriteria = new SiteNumber();
            Pager = new Pager();
        }
        public List<SiteNumber> SiteNumbers { get; set; }
        public SiteNumber SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}