using Data;
using Models;

namespace Gateway.Services;

public interface IAuditService
{
    Task LogLogin(string? userId, string email, bool successful, string? reason = null, string? ipAddress = null, DateTime? timestamp = null);
    Task LogAction(string action, string? details = null, string? userId = null, string? ipAddress = null, DateTime? timestamp = null);
}

public sealed class AuditService : IAuditService
{
    private readonly SsoDbContext _db;
    public AuditService(SsoDbContext db) => _db = db;

    public async Task LogLogin(string? userId, string email, bool successful, string? reason = null, string? ipAddress = null, DateTime? timestamp = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = successful ? "LoginSuccess" : "LoginFailed",
            Details = successful ? $"Successful login for {email}." : $"Failed login for {email}. Reason: {reason ?? "Unknown"}.",
            Email = email,
            Reason = successful ? null : (reason ?? "Unknown"),
            UserId = userId,
            IpAddress = ipAddress,
            Timestamp = timestamp ?? DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task LogAction(string action, string? details = null, string? userId = null, string? ipAddress = null, DateTime? timestamp = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            Details = details,
            UserId = userId,
            IpAddress = ipAddress,
            Timestamp = timestamp ?? DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
