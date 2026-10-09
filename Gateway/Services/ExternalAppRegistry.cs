using Microsoft.Extensions.DependencyInjection;
using Data;
using Gateway.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Services;

/// <summary>
/// A returnUrl that matched an enabled, registered app. <see cref="Url"/> is rebuilt
/// from the parsed URI, so the address we redirect to is exactly the address we validated.
/// </summary>
public sealed record ApprovedReturnTarget(ExternalApp App, string Url);

/// <summary>
/// Source of truth for which client apps may receive a token. Also feeds the CORS policy.
/// </summary>
public interface IExternalAppRegistry
{
    /// <summary>Backing list. The Apps admin pages add, edit, toggle and remove entries here.</summary>
    List<ExternalApp> Apps { get; }

    /// <summary>Returns the matching target, or null when the URL isn't approved.</summary>
    ApprovedReturnTarget? TryResolve(string? returnUrl);

    /// <summary>True when the origin (scheme://host[:port]) belongs to an enabled app.</summary>
    bool IsOriginApproved(string? origin);
}

public class InMemoryExternalAppRegistry : IExternalAppRegistry
{
    public virtual List<ExternalApp> Apps { get; } = new()
    {
        new ExternalApp
        {
            Id = 1,
            Name = "Sample App",
            ReturnUrl = "https://example.com/callback",
            IsEnabled = true
        }
    };

    public ApprovedReturnTarget? TryResolve(string? returnUrl)
    {
        if (!TryParseHttpUri(returnUrl, out var candidate))
        {
            return null;
        }

        // user:pass@host tricks and fragments have no business in a callback address.
        if (candidate.UserInfo.Length > 0 || candidate.Fragment.Length > 0)
        {
            return null;
        }

        // A caller-supplied "token" parameter would sit next to ours. Apps that read the
        // first value could end up accepting an attacker's token, so refuse it outright.
        if (QueryHelpers.ParseQuery(candidate.Query).ContainsKey("token"))
        {
            return null;
        }

        foreach (var app in Apps.ToArray())
        {
            if (!app.IsEnabled || !TryParseHttpUri(app.ReturnUrl, out var registered))
            {
                continue;
            }

            if (SameOrigin(candidate, registered) && SamePath(candidate, registered))
            {
                return new ApprovedReturnTarget(app, candidate.GetLeftPart(UriPartial.Path) + candidate.Query);
            }
        }

        return null;
    }

    public bool IsOriginApproved(string? origin)
    {
        if (!TryParseHttpUri(origin, out var candidate))
        {
            return false;
        }

        return Apps.ToArray().Any(app =>
            app.IsEnabled
            && TryParseHttpUri(app.ReturnUrl, out var registered)
            && SameOrigin(candidate, registered));
    }

    private static bool TryParseHttpUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.GetLeftPart(UriPartial.Authority), b.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(Uri a, Uri b) =>
        string.Equals(a.AbsolutePath.TrimEnd('/'), b.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);
}

/// <summary>
/// The registry the running site uses. Reads the apps that admins manage under Admin > Apps
/// (the TenantApps table), so the login gateway and the admin screens always agree.
/// Read-only: add, edit, enable and remove apps through the admin pages.
/// </summary>
public sealed class DbExternalAppRegistry : InMemoryExternalAppRegistry
{
    private readonly IServiceScopeFactory _scopes;

    public DbExternalAppRegistry(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public override List<ExternalApp> Apps
    {
        get
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();

            return db.TenantApps.AsNoTracking()
                .OrderBy(a => a.Id)
                .Select(a => new ExternalApp
                {
                    Id = a.Id,
                    Name = a.Name,
                    ReturnUrl = a.ReturnUrl,
                    IsEnabled = a.IsEnabled
                })
                .ToList();
        }
    }
}
