using Microsoft.AspNetCore.Identity;

namespace Models;
public class AuditLog
{
    public int Id { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? Details { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string? IpAddress { get; set; }

    public string? UserId { get; set; }
    public IdentityUser? User { get; set; }

    public int? TenantAppId { get; set; }
    public TenantApp? TenantApp { get; set; }
}