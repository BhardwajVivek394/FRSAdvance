using System;

namespace Domain.SMS
{
    public class OtpResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public class OtpData
    {
        public string Otp { get; set; }
        public string Mobile { get; set; }
        public DateTime ExpiryTime { get; set; }
        public int Attempts { get; set; }
        public string MacAddress { get; set; }
        public int UserId { get; set; }
    }
}
