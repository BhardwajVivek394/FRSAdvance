using System.Collections.Generic;

namespace Domain
{
    public class APIResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public dynamic Value { get; set; }
        public List<string> Result { get; set; }
        public APIResponse()
        {
            IsSuccess = false;
            Message = string.Empty;
        }
    }
}