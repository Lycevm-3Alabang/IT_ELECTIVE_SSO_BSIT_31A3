using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Areas.Admin.Models.TenantApps
{
    /// <summary>Create/Edit form model.</summary>
    public class TenantAppViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "App name is required.")]
        [StringLength(100, ErrorMessage = "App name cannot exceed 100 characters.")]
        [Remote("ValidateName", "TenantApp", "Admin", AdditionalFields = nameof(Id),
            ErrorMessage = "An app with this name already exists.")]
        [Display(Name = "App name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Return URL is required.")]
        [StringLength(2048, ErrorMessage = "Return URL is too long.")]
        [HttpUrl]
        [Display(Name = "Return URL")]
        public string ReturnUrl { get; set; } = string.Empty;

        [Display(Name = "Enabled")]
        public bool IsEnabled { get; set; } = true;
    }

    public class TenantAppListItemViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public int GroupCount { get; set; }
    }

    public class TenantAppListViewModel
    {
        public IReadOnlyList<TenantAppListItemViewModel> Apps { get; set; } = Array.Empty<TenantAppListItemViewModel>();
    }
}
