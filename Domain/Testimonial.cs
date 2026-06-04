using System.Collections.Generic;

namespace Domain
{
    public class Testimonial
    {
        public int Id { get; set; }
        public int ZoneId { get; set; }
        public int DivisionId { get; set; }
        public int SiteId { get; set; }
        public int AssetTypeId { get; set; }
        public int AssetId { get; set; }
        public string Remark { get; set; }
        public string RailwayRemark { get; set; }
        public int CreatedBy { get; set; }

        //Extra
        public string Site { get; set; }
        public string Asset { get; set; }
        public string AssetType { get; set; }
        public string Division { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public List<TestimonialImage> TestimonialImages { get; set; } = new List<TestimonialImage>();
    }

    public class TestimonialLister : APIResponse
    {
        public TestimonialLister()
        {
            Testimonials = new List<Testimonial>();
            SearchCriteria = new Testimonial();
            Pager = new Pager();
        }
        public List<Testimonial> Testimonials { get; set; }
        public Testimonial SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
