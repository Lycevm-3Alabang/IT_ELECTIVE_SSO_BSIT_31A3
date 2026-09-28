using System.ComponentModel.DataAnnotations;

namespace Gateway.Models
{
    public class ExternalApp
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "App name is required.")]
        [StringLength(100, ErrorMessage = "App name cannot exceed 100 characters.")]
        [Display(Name = "Application Name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Return URL is required.")]
        [Url(ErrorMessage = "Please enter a valid URL (e.g., https://example.com/callback).")]
        [Display(Name = "Return URL")]
        public string ReturnUrl { get; set; } = string.Empty;

        [Display(Name = "Status")]
        public bool IsEnabled { get; set; } = true;
    }
}