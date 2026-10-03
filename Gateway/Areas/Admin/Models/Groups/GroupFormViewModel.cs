using System.ComponentModel.DataAnnotations;
using Models;

namespace Gateway.Areas.Admin.Models.Groups;

/// <summary>An app the admin can pick in the Create form.</summary>
public record AppOption(int Id, string Name, bool IsEnabled);

/// <summary>Used by both the Create and Edit forms.</summary>
public class GroupFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Select the app this group belongs to.")]
    [Display(Name = "App")]
    public int? TenantAppId { get; set; }

    /// <summary>
    /// What the admin types. The app prefix is added on save, so this is only
    /// the "[GroupName]" part of "[AppName]-[GroupName]".
    /// </summary>
    [Required(ErrorMessage = "Group name is required.")]
    [StringLength(GroupRules.MaxNameLength, ErrorMessage = "Group name can be at most {1} characters.")]
    [Display(Name = "Group name")]
    public string GroupName { get; set; } = string.Empty;

    /// <summary>
    /// Nullable on purpose: an empty field must be rejected, not silently bound
    /// to 0, because 0 is the HIGHEST power level.
    /// </summary>
    [Required(ErrorMessage = "Power level is required.")]
    [Range(GroupRules.MinPowerLevel, GroupRules.MaxPowerLevel,
        ErrorMessage = "Power level must be a whole number from {1} (highest power) to {2} (lowest power).")]
    [Display(Name = "Power level")]
    public int? PowerLevel { get; set; }

    // ---- Display only (filled in by the controller, never trusted from the form) ----

    public string AppName { get; set; } = string.Empty;

    public IReadOnlyList<AppOption> Apps { get; set; } = Array.Empty<AppOption>();
}
