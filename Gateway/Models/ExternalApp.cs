using System.ComponentModel.DataAnnotations;

namespace Gateway.Models
{
    public class ExternalApp
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "App name is required.")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Return URL is required.")]
        [Url(ErrorMessage = "Please enter a valid URL.")]
        public string ReturnUrl { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;
    }
}