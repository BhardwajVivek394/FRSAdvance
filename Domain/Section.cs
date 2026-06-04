using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Section
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public int CreatedBy { get; set; }
        public int DivisionId { get; set; }


        //extra
        public string DivisionName { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }

        public Section()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class SectionLister
    {
        public SectionLister()
        {
            mSections = new List<Section>();
            SearchCriteria = new Section();
            Pager = new Pager();
        }
        public List<Section> mSections { get; set; }
        public Section SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
