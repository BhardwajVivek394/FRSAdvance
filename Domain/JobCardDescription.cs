using System;

namespace Domain
{
    public class JobCardDescription
    {
        public int Id { get; set; }
        public int JobCardId { get; set; }
        public string DailyWork { get; set; }
        public string Remark { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
