using System.Collections.Generic;

namespace Domain
{
    public class UserClass : APIResponse
    {
        public int Id { get; set; }
        public string ClassName { get; set; }
        public string Description { get; set; }
        public int DelayInMinutes { get; set; }

        //extra
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public UserClass()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class UserClassLister : APIResponse
    {
        public UserClassLister()
        {
            UserClasses = new List<UserClass>();
            SearchCriteria = new UserClass();
            Pager = new Pager();
        }
        public List<UserClass> UserClasses { get; set; }
        public UserClass SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}