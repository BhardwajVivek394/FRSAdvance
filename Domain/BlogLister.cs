using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Blog
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }

        public bool IsMyDaily { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public string SearchText { get; set; }
        public List<BlogImage> BlogImages { get; set; }
        public List<BlogLikeLine> BlogLikeLines { get; set; }
        public List<BlogCommentLine> BlogCommentLines { get; set; }
        public Blog()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }

    public class BlogLister
    {
        public BlogLister()
        {
            Blogs = new List<Blog>();
            SearchCriteria = new Blog();
            Pager = new Pager();
        }
        public List<Blog> Blogs { get; set; }
        public Blog SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
