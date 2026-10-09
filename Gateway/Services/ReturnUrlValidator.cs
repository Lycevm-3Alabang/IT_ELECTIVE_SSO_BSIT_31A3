using Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Services;

/// <summary>
/// Gatekeeps the SSO login page so it can only hand control back to apps
/// that have been registered by an admin. A returnUrl that isn't an exact,
/// case-insensitive match for an enabled TenantApp.ReturnUrl is rejected and
/// the attempt is written to AuditLogs so unexpected callers show up in the
/// admin audit trail.
/// </summary>
public interface IReturnUrlValidator
{
    /// <summary>
    /// Pulls the raw "returnUrl" value out of the request's query string.
    /// Returns null when the key is missing or blank.
    /// </summary>
    string? ExtractReturnUrl(IQueryCollection query);

    /// <summary>
    /// Looks up the TenantApp whose ReturnUrl matches the given value.
    /// Returns null (and logs an AuditLogs entry) when returnUrl is
    /// non-empty but doesn't match any enabled, registered app. A null or
    /// blank returnUrl is treated as "no external app involved" and is
    /// returned as null without logging anything.
    /// </summary>
    Task<TenantApp?> ValidateAsync(string? returnUrl, string? ipAddress = null);
}

public sealed class ReturnUrlValidator : IReturnUrlValidator
{
    private readonly SsoDbContext _db;
    private readonly IAuditService _auditService;

    public ReturnUrlValidator(SsoDbContext db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    public string? ExtractReturnUrl(IQueryCollection query)
    {
        if (query is null || !query.TryGetValue("returnUrl", out var value))
        {
            return null;
        }

        var returnUrl = value.ToString();
        return string.IsNullOrWhiteSpace(returnUrl) ? null : returnUrl;
    }

    public async Task<TenantApp?> ValidateAsync(string? returnUrl, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            // Nothing was supplied, so there's no external app to validate.
            // This is a normal, direct visit to the login page.
            return null;
        }

        var normalized = returnUrl.Trim();

        var match = await _db.TenantApps
            .Where(a => a.IsEnabled && a.ReturnUrl != null)
            .FirstOrDefaultAsync(a => a.ReturnUrl!.ToLower() == normalized.ToLower());

        if (match is null)
        {
            await _auditService.LogAction(
                "InvalidReturnUrl",
                $"Rejected unregistered or disabled return URL: {normalized}",
                userId: null,
                ipAddress: ipAddress);
        }

        return match;
    }
}
