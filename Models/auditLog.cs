namespace Models;
public class AuditLog
{
    public int Id { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? Details { get; set; }

    /// <summary>
    /// Email associated with the event. Set for login attempts (success or
    /// failure) even when no matching user exists, so failed logins for
    /// unknown emails are still traceable.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Why a login failed (e.g. "Invalid email or password", "Account inactive",
    /// "Locked out"). Null for successful logins and admin actions.
    /// </summary>
    public string? Reason { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string? IpAddress { get; set; }

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public int? TenantAppId { get; set; }
    public TenantApp? TenantApp { get; set; }
}