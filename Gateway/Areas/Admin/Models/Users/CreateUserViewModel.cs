using System.ComponentModel.DataAnnotations;

namespace Gateway.Areas.Admin.Models.Users
{
    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Temporary password is required.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least {2} characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Temporary Password")]
        public string TemporaryPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm the password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare(nameof(TemporaryPassword), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
