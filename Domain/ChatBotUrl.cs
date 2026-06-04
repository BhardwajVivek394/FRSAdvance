using System.Collections.Generic;

namespace Domain
{
    public class ChatBotUrl
    {
        public int Id { get; set; }
        public int SearchId { get; set; }
        public int? ZoneId { get; set; }
        public int? DivisionId { get; set; }
        public string Url { get; set; }
        public bool IsApprove { get; set; }
        public int CreatedBy { get; set; }
        public int LastModifiedBy { get; set; }

        //Extra Properties
        public string DivisionName { get; set; }
        public string ZoneName { get; set; }
    }
    public class ChatBotUrlLister
    {
        public ChatBotUrlLister()
        {
            mChatBotUrls = new List<ChatBotUrl>();
            SearchCriteria = new ChatBotUrl();
            Pager = new Pager();
        }
        public List<ChatBotUrl> mChatBotUrls { get; set; }
        public ChatBotUrl SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
