using System.Collections.Generic;

namespace Domain
{
    public class ChatBotFalseAnswer
    {
        public int Id { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
        public int CreatedBy { get; set; }

        //Extra
        public string ColumnName { get; set; }
        public string SortDirection { get; set; }
        public string UserName { get; set; }
    }

    public class ChatBotFalseAnswerLister
    {
        public ChatBotFalseAnswerLister()
        {
            ChatBotFalseAnswers = new List<ChatBotFalseAnswer>();
            SearchCriteria = new ChatBotFalseAnswer();
            Pager = new Pager();
        }
        public List<ChatBotFalseAnswer> ChatBotFalseAnswers { get; set; }
        public ChatBotFalseAnswer SearchCriteria { get; set; }
        public Pager Pager { get; set; }
    }
}
