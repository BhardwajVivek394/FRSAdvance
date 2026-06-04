using System.Collections.Generic;

namespace Domain
{
    public class AIRemark
    {
        public int Id { get; set; }
        public int AssetTypeId { get; set; }
        public string Remark { get; set; }
        public string AssetType { get; set; }
        public int CreatedBy { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public int ClusterId { get; set; }
        public AIRemark()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }
    public class AIRemarkLister
    {
        public AIRemarkLister()
        {
            mAIRemarks = new List<AIRemark>();
            SearchCriteria = new AIRemark();
            Pager = new Pager();
        }
        public List<AIRemark> mAIRemarks { get; set; }
        public AIRemark SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
