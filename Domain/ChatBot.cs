using System.Collections.Generic;

namespace Domain
{
    public class ChatBot
    {
        public int UserId { get; set; }
        public string Message { get; set; }
        public string Name { get; set; }
        public int CreatedBy { get; set; }

        //Extra
        public string FileBase64 { get; set; }
        public int SearchId { get; set; }
        public int? ZoneId { get; set; }
        public int? DivisionId { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public string Status { get; set; }
        public string Question { get; set; }
        public string MessageType { get; set; }
        public List<File> Files { get; set; }
        public string FilePath { get; set; }
    }

    public class E7ChatBot
    {
        public string checkpoint_id { get; set; }
        public string message { get; set; }
        public bool reset_memory { get; set; }

    }

    public class File
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
    }

    public class DivisionPdfResponse
    {
        public string Text { get; set; }
        public List<File> Files { get; set; }
    }

}
