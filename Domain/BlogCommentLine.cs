using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class BlogCommentLine
    {
        public int Id { get; set; }
        public int BlogId { get; set; }
        public string Comment { get; set; }
        public int CreatedBy { get; set; }

        //extra
        public string ProfileImage { get; set; }
        public string UserName { get; set; }
    }

}
