using System.Security.Claims;
using Gateway.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Models;

namespace Gateway.Controllers;

[Route("Account")]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;

    public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IAuditService auditService)
    {
        _signInManager = signInManager; _userManager = userManager; _auditService = auditService;
    }

    [HttpGet("Login")]
    public IActionResult Login(string? returnUrl = null) { ViewData["ReturnUrl"] = returnUrl; return View(); }

    [HttpPost("Login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, bool rememberMe = false, string? returnUrl = null)
    {
        var normalizedEmail = email?.Trim() ?? string.Empty;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var user = await _userManager.FindByEmailAsync(normalizedEmail);

        if (user is not null && !user.IsActive)
        {
            await _auditService.LogLogin(user.Id, normalizedEmail, false, "Account inactive", ip);
            ModelState.AddModelError(string.Empty, AuthenticationMessages.AccountSuspended);
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        var result = await _signInManager.PasswordSignInAsync(normalizedEmail, password, rememberMe, false);
        if (result.Succeeded)
        {
            user ??= await _userManager.FindByEmailAsync(normalizedEmail);
            if (user is not null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
                await _auditService.LogLogin(user.Id, normalizedEmail, true, ipAddress: ip);
            }
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        var reason = result.IsLockedOut ? "Locked out" : result.IsNotAllowed ? "Not allowed" : "Invalid email or password";
        await _auditService.LogLogin(user?.Id, normalizedEmail, false, reason, ip);
        ModelState.AddModelError(string.Empty, reason);
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost("Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.Identity?.Name;
        await _signInManager.SignOutAsync();
        await _auditService.LogAction("Logout", $"User {email ?? userId ?? "unknown"} logged out.", userId, HttpContext.Connection.RemoteIpAddress?.ToString());
        return RedirectToAction("Index", "Home");
    }
}
