using System.Collections.Generic;

namespace Domain
{
    public class PointMachineException
    {
        public int Id { get; set; }
        public int AssetId { get; set; }
        public string Direction { get; set; }
        public string ArrayData { get; set; }
        public string TsTime { get; set; }
        public System.DateTime TimeStamp { get; set; }
        public int Cluster { get; set; }
        public string Type { get; set; }
        public string Remark { get; set; }
        public bool IsDivisionPDF { get; set; }
        public int SiteId { get; set; }

        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public string Division { get; set; }
        public string Site { get; set; }
        public string Name { get; set; }
    }

    public class PointMachineExceptionLister
    {
        public PointMachineExceptionLister()
        {
            PointMachineExceptions = new List<PointMachineException>();
            SearchCriteria = new PointMachineException();
            Pager = new Pager();
        }
        public List<PointMachineException> PointMachineExceptions { get; set; }
        public PointMachineException SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
