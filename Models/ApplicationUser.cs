using Microsoft.AspNetCore.Identity;

namespace Models;

/// <summary>
/// Extends the default Identity user with the fields the SSO master plan
/// requires (IsActive, CreatedAt, LastLoginAt). Issue 5 (User Active Toggle)
/// and Issue 16 (Login Gateway) both read IsActive, so this needs to exist
/// before those land.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }
}
