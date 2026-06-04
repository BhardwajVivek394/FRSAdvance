using System.ComponentModel.DataAnnotations;

namespace Domain
{
    public class ChangePassword
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Old password required.")]
        public string OldPassword { get; set; }
        [Required(ErrorMessage = "New password required.")]
        public string NewPassword { get; set; }
        [Required(ErrorMessage = "Confirm password required.")]
        [Compare("NewPassword", ErrorMessage = "New password and confirm password does not match.")]
        public string ConfirmPassword { get; set; }
    }
}