using System.Collections.Generic;

namespace Domain
{
    public class SupportTicketImage
    {
        public int Id { get; set; }
        public int SupportTicketId { get; set; }
        public string ImagePath { get; set; }
        public int CreatedBy { get; set; }
        public System.DateTime CreatedDate { get; set; }
        public int LastModifiedBy { get; set; }
        //extra
        public string SearchText { get; set; }
        public string SupportTicketName { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public string FileBase64 { get; set; }
        public string FileExtension { get; set; }
        public SupportTicketImage()
        {
            ColumnName = "Id";
            SortDirection = "DESC";
        }
    }
    public class SupportTicketImageLister : APIResponse
    {
        public SupportTicketImageLister()
        {
            mSupportTicketImages = new List<SupportTicketImage>();
            SearchCriteria = new SupportTicketImage();
            Pager = new Pager();
        }
        public List<SupportTicketImage> mSupportTicketImages { get; set; }
        public SupportTicketImage SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
