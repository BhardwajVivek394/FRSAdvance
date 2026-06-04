using System.Collections.Generic;

namespace Domain
{
    public class ChatBotRemark
    {
        public int Id { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
        public string Remark { get; set; }
        public int CreatedBy { get; set; }
        public string UserName { get; set; }
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
    }
    public class ChatBotRemarkLister
    {
        public ChatBotRemarkLister()
        {
            ChatBotRemarks = new List<ChatBotRemark>();
            SearchCriteria = new ChatBotRemark();
            Pager = new Pager();
        }
        public List<ChatBotRemark> ChatBotRemarks { get; set; }
        public ChatBotRemark SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
