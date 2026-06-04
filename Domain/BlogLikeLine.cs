using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class BlogLikeLine
    {
        public int Id { get; set; }
        public int BlogId { get; set; }
        public int LikeCounter { get; set; }
        public int CreatedBy { get; set; }
    }
}
