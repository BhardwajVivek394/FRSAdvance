using System.Collections.Generic;

namespace Domain
{
    public class SupportTicket
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public System.DateTime RequestDate { get; set; }
        public int StatusId { get; set; }
        public int CreatedBy { get; set; }
        public string Remark { get; set; }
        public System.DateTime CreatedDate { get; set; }

        //extra
        public List<SupportTicketImage> SupportTicketImages { get; set; }
        public string SearchText { get; set; }
        public string UserName { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public int RoleId { get; set; }
        public string FileBase64 { get; set; }
        public string FileExtension { get; set; }

        public SupportTicket()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }
    public class SupportTicketLister : APIResponse
    {
        public SupportTicketLister()
        {
            mSupportTickets = new List<SupportTicket>();
            SearchCriteria = new SupportTicket();
            Pager = new Pager();
        }
        public List<SupportTicket> mSupportTickets { get; set; }
        public SupportTicket SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
