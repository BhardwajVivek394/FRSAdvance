using System.ComponentModel.DataAnnotations;

namespace Domain
{
    public class Login
    {
        [Required(ErrorMessage = "Email address is required")]
        public string EmailAddress { get; set; }

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; }
        public bool RememberMe { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string Source { get; set; }
        public bool IsSendOTP { get; set; }
    }
}