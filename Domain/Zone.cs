using System;
using System.Collections.Generic;

namespace Domain
{
    public class Zone : APIResponse
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public int? DeletedBy { get; set; }
        public DateTime? DeletedDate { get; set; }
        public string RepresentationCode { get; set; }

        //extra
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public Zone()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class ZoneLister : APIResponse
    {
        public ZoneLister()
        {
            mZones = new List<Zone>();
            SearchCriteria = new Zone();
            Pager = new Pager();
        }
        public List<Zone> mZones { get; set; }
        public Zone SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}