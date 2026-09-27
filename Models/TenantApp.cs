using System.ComponentModel.DataAnnotations;

namespace Models;

public class TenantApp
{
    public int Id { get; set; }

    [Required(ErrorMessage = "App name is required.")]
    [StringLength(100, ErrorMessage = "App name must be 100 characters or fewer.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Return URL is required.")]
    [Url(ErrorMessage = "Enter a valid URL, e.g. https://example.com/callback")]
    [StringLength(500, ErrorMessage = "Return URL must be 500 characters or fewer.")]
    public string ReturnUrl { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<Group> Groups { get; set; } = new List<Group>();
}
