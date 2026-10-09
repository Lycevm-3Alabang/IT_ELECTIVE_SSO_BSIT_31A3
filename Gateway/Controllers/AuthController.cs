using Data;
using Gateway.Models;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Controllers;

/// <summary>
/// Central login for client apps. A user arrives from an app with ?returnUrl=..., signs in here,
/// and is sent back to that app with a signed JWT in ?token=.
///
/// This is separate from AccountController, which signs admins into the gateway itself with a cookie.
/// Nothing here creates a gateway session; the JWT is the only thing that leaves.
/// </summary>
[AllowAnonymous]
[Route("Auth")]
public class AuthController : Controller
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";
    private const int MaxLoggedUrlLength = 200;

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SsoDbContext _db;
    private readonly IExternalAppRegistry _apps;
    private readonly IJwtTokenService _jwt;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IAuditService _audit;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        SsoDbContext db,
        IExternalAppRegistry apps,
        IJwtTokenService jwt,
        ILoginRateLimiter rateLimiter,
        IAuditService audit)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
        _apps = apps;
        _jwt = jwt;
        _rateLimiter = rateLimiter;
        _audit = audit;
    }

    [HttpGet("Login")]
    public IActionResult Login(string? returnUrl = null)
    {
        var target = _apps.TryResolve(returnUrl);
        if (target is null)
        {
            return InvalidReturnUrl();
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl, AppName = target.App.Name });
    }

    [HttpPost("Login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var email = (model.Email ?? string.Empty).Trim();

        // The hidden field can be edited, so the address is checked again before any credential work.
        var target = _apps.TryResolve(model.ReturnUrl);
        if (target is null)
        {
            await _audit.LogAction(
                "LoginBlocked",
                $"Login for {email} blocked: returnUrl not approved ({Truncate(model.ReturnUrl)}).",
                userId: null,
                ipAddress: ip);
            return InvalidReturnUrl();
        }

        // Keep the password out of the model that goes back to the view.
        var password = model.Password ?? string.Empty;
        model.AppName = target.App.Name;
        model.Password = string.Empty;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var tenantApp = await _db.TenantApps.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == target.App.Name);

        var limit = _rateLimiter.Check(ip, email);
        if (limit.IsBlocked)
        {
            await _audit.LogLogin(null, email, false, "Rate limited", ip, tenantAppId: tenantApp?.Id);

            var minutes = Math.Max(1, (int)Math.Ceiling(limit.RetryAfter.TotalMinutes));
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = ((int)Math.Ceiling(limit.RetryAfter.TotalSeconds)).ToString();
            ModelState.AddModelError(string.Empty,
                $"Too many failed sign-in attempts. Try again in {minutes} {(minutes == 1 ? "minute" : "minutes")}.");
            return View(model);
        }

        return await SignInAsync(model, target, tenantApp, email, ip, password);
    }

    private async Task<IActionResult> SignInAsync(
        LoginViewModel model, ApprovedReturnTarget target, TenantApp? tenantApp,
        string email, string? ip, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return await FailAsync(model, null, email, ip, tenantApp, "Invalid email or password", InvalidCredentialsMessage);
        }

        if (!user.IsActive)
        {
            // Only tell someone their account is suspended if they proved they own it.
            // Otherwise anyone could probe which emails are registered and disabled.
            var passwordCorrect = await _userManager.CheckPasswordAsync(user, password);
            return passwordCorrect
                ? await FailAsync(model, user, email, ip, tenantApp, "Account inactive", AuthenticationMessages.AccountSuspended)
                : await FailAsync(model, user, email, ip, tenantApp, "Invalid email or password", InvalidCredentialsMessage);
        }

        // Checks the password without issuing a cookie. Lockout stays off here; the rate limiter handles guessing.
        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return result switch
            {
                { IsLockedOut: true } => await FailAsync(model, user, email, ip, tenantApp,
                    "Locked out", "This account is temporarily locked. Try again later."),
                { IsNotAllowed: true } => await FailAsync(model, user, email, ip, tenantApp,
                    "Not allowed", "This account isn't allowed to sign in. Contact an administrator."),
                _ => await FailAsync(model, user, email, ip, tenantApp,
                    "Invalid email or password", InvalidCredentialsMessage)
            };
        }

        // Only the groups this user holds in the app they're signing in to go into the token.
        var memberships = tenantApp is null
            ? new List<(string Name, int Level)>()
            : (await _db.UserGroups.AsNoTracking()
                    .Where(ug => ug.UserId == user.Id && ug.Group.TenantAppId == tenantApp.Id)
                    .OrderBy(ug => ug.Group.Name)
                    .Select(ug => new { ug.Group.Name, ug.Group.PowerLevel })
                    .ToListAsync())
                .Select(g => (Name: g.Name, Level: g.PowerLevel))
                .ToList();

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        await _audit.LogLogin(user.Id, email, true, ipAddress: ip, tenantAppId: tenantApp?.Id);

        var token = _jwt.CreateToken(
            user,
            target.App.Name,
            memberships.Select(m => m.Name).ToList(),
            memberships.Select(m => m.Level).ToList());

        // The token travels in the URL, so keep it out of caches and Referer headers.
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        return Redirect(QueryHelpers.AddQueryString(target.Url, "token", token));
    }

    private async Task<IActionResult> FailAsync(
        LoginViewModel model, ApplicationUser? user, string email, string? ip,
        TenantApp? tenantApp, string auditReason, string message)
    {
        _rateLimiter.RecordFailure(ip, email);
        await _audit.LogLogin(user?.Id, email, false, auditReason, ip, tenantAppId: tenantApp?.Id);
        ModelState.AddModelError(string.Empty, message);
        return View(model);
    }

    private IActionResult InvalidReturnUrl()
    {
        var view = View("InvalidReturnUrl");
        view.StatusCode = StatusCodes.Status400BadRequest;
        return view;
    }

    private static string Truncate(string? value) =>
        value is null ? "none"
        : value.Length <= MaxLoggedUrlLength ? value
        : value[..MaxLoggedUrlLength] + "...";
}
