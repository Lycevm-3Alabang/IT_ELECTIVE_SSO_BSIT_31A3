using System.ComponentModel.DataAnnotations;

namespace Gateway.Models.Admin;

public class AppListItemViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public class AppListViewModel
{
    public IReadOnlyList<AppListItemViewModel> Apps { get; set; } = Array.Empty<AppListItemViewModel>();
}

public class CreateAppViewModel
{
    [Required(ErrorMessage = "App name is required.")]
    [StringLength(100, ErrorMessage = "App name must be 100 characters or fewer.")]
    [Display(Name = "App Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Return URL is required.")]
    [Url(ErrorMessage = "Enter a valid URL, e.g. https://example.com/callback")]
    [StringLength(500, ErrorMessage = "Return URL must be 500 characters or fewer.")]
    [Display(Name = "Return URL")]
    public string ReturnUrl { get; set; } = string.Empty;
}

public class EditAppViewModel : CreateAppViewModel
{
    public int Id { get; set; }
}
