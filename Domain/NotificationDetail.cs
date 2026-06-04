using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class NotificationDetail
    {
        public int FRSAlertId { get; set; }
        public int UserId { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public int UserClassId { get; set; }
        public string PhoneNumber { get; set; }
        public string UserClassName { get; set; }
        public string PersonName { get; set; }  // ← enriched server-side
    }
}
