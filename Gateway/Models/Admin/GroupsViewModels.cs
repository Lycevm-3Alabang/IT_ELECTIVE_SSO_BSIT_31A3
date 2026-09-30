using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Gateway.Models.Admin;

public class GroupListItemViewModel
{
    public int Id { get; set; }

    /// <summary>Fully-qualified, app-prefixed name (e.g. "SalesApp-Admin").</summary>
    public string Name { get; set; } = string.Empty;

    public string AppName { get; set; } = string.Empty;

    public int PowerLevel { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class GroupListViewModel
{
    public IReadOnlyList<GroupListItemViewModel> Groups { get; set; } = Array.Empty<GroupListItemViewModel>();
}

public class CreateGroupViewModel
{
    [Required(ErrorMessage = "Select an app.")]
    [Display(Name = "App")]
    public int TenantAppId { get; set; }

    [Required(ErrorMessage = "Group name is required.")]
    [StringLength(100, ErrorMessage = "Group name must be 100 characters or fewer.")]
    [Display(Name = "Group name")]
    public string GroupName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Level is required.")]
    [Range(0, 100, ErrorMessage = "Level must be between 0 (highest power) and 100.")]
    [Display(Name = "Level")]
    public int PowerLevel { get; set; }

    public List<SelectListItem> TenantAppOptions { get; set; } = new();
}

public class EditGroupViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Select an app.")]
    [Display(Name = "App")]
    public int TenantAppId { get; set; }

    [Required(ErrorMessage = "Group name is required.")]
    [StringLength(100, ErrorMessage = "Group name must be 100 characters or fewer.")]
    [Display(Name = "Group name")]
    public string GroupName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Level is required.")]
    [Range(0, 100, ErrorMessage = "Level must be between 0 (highest power) and 100.")]
    [Display(Name = "Level")]
    public int PowerLevel { get; set; }

    public List<SelectListItem> TenantAppOptions { get; set; } = new();
}
