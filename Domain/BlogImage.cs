using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class BlogImage
    {
        public int Id { get; set; }
        public int BlogId { get; set; }
        public string ImagePath { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public int ModifiedBy { get; set; }

        //Extra
        public string FileBase64 { get; set; }
        public string FileExtension { get; set; }
        public string SearchText { get; set; }

        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public int RoleId { get; set; }

        public BlogImage()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class BlogImageLister
    {
        public BlogImageLister()
        {
            BlogImages = new List<BlogImage>();
            SearchCriteria = new BlogImage();
            Pager = new Pager();
        }
        public List<BlogImage> BlogImages { get; set; }
        public BlogImage SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
