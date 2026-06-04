using System.IO;

namespace E7FRSAdvance.Utility
{
    public class FileReader
    {
        private readonly string filePath;
        public FileReader(string filePath)
        {
            this.filePath = filePath;
        }

        public string Read()
        {
            string emailTemplate = "";
            emailTemplate = GetFileContents(this.filePath);
            return emailTemplate;
        }

        private string GetFileContents(string FullPath)
        {
            string strContents = null;
            StreamReader objReader = default(StreamReader);
            objReader = new StreamReader(FullPath);
            strContents = objReader.ReadToEnd();
            objReader.Close();
            return strContents;
        }
    }
}
