using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Learning
    {
        public int Id { get; set; }
        public string Subject { get; set; }
        public string Description { get; set; }
        public string VideoURL { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        public System.DateTime LastModifiedDate { get; set; }
        public Nullable<int> DeletedBy { get; set; }
        public Nullable<System.DateTime> DeletedDate { get; set; }

        //extra 
        public string SearchText { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public Learning()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class LearningLister : APIResponse
    {
        public LearningLister()
        {
            mLearnings = new List<Learning>();
            SearchCriteria = new Learning();
            Pager = new Pager();
        }
        public List<Learning> mLearnings { get; set; }
        public Learning SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
