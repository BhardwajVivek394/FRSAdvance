using System.Collections.Generic;

namespace Domain
{
    public class Role : APIResponse
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public string Title { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        public System.DateTime LastModifiedDate { get; set; }
        public int? DeletedBy { get; set; }
        public System.DateTime? DeletedDate { get; set; }

        //extra
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public Role()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class RoleLister : APIResponse
    {
        public RoleLister()
        {
            Roles = new List<Role>();
            SearchCriteria = new Role();
            Pager = new Pager();
        }
        public List<Role> Roles { get; set; }
        public Role SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}