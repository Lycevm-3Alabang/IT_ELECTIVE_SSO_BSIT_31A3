using System.Security.Claims;
using System.Security.Cryptography;
using Gateway.Areas.Admin.Models.Users;
using Gateway.Services;
using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Areas.Admin.Controllers;

public class UsersController : AdminBaseController
{
    private const int MaxPageSize = 100;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SsoDbContext _db;
    private readonly IAuditService _auditService;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        SsoDbContext db,
        IAuditService auditService)
    {
        _userManager = userManager;
        _db = db;
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, int pageSize = 10)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _userManager.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(u => u.NormalizedEmail != null && u.NormalizedEmail.Contains(term));
        }

        query = query.OrderBy(u => u.Email);
        var totalCount = await query.CountAsync();
        var users = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new UserListItemViewModel
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            }).ToListAsync();

        return View(new UserListViewModel
        {
            SearchTerm = search,
            Users = users,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateUserViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var email = model.Email.Trim();
        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "A user with this email already exists.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.TemporaryPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _auditService.LogAction(
            "CreateUser",
            $"Admin created user {email}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["StatusMessage"] = $"User {email} was created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var groups = await _db.UserGroups
            .Where(ug => ug.UserId == id)
            .Include(ug => ug.Group)
            .Select(ug => ug.Group.Name)
            .ToListAsync();

        var recentActivity = await _db.AuditLogs.AsNoTracking()
            .Where(x => x.UserId == id)
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.Id)
            .Take(10)
            .Select(x => new AuditLogEntry
            {
                UserId = id,
                Action = x.Action,
                PerformedBy = x.Email ?? x.UserId ?? "system",
                Timestamp = x.Timestamp
            }).ToListAsync();

        return View(new UserDetailsViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Groups = groups,
            RecentActivity = recentActivity
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.IsActive = !user.IsActive;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            if (IsAjaxRequest()) return StatusCode(500, new { success = false, message = errors });
            TempData["StatusMessage"] = errors;
            return RedirectToAction(nameof(Index));
        }

        await _auditService.LogAction(
            "ToggleActive",
            $"Admin changed {user.Email} to {(user.IsActive ? "active" : "inactive")}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (IsAjaxRequest())
        {
            return Json(new { success = true, id = user.Id, isActive = user.IsActive, status = user.IsActive ? "Active" : "Inactive" });
        }

        TempData["StatusMessage"] = $"User {user.Email} is now {(user.IsActive ? "active" : "inactive")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.IsActive = false;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = "Failed to deactivate the user: " + string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        await _auditService.LogAction(
            "DeleteUser",
            $"Admin soft-deleted/deactivated {user.Email}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["StatusMessage"] = $"User {user.Email} was deactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var temporaryPassword = GenerateTemporaryPassword();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, temporaryPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            if (IsAjaxRequest()) return StatusCode(500, new { success = false, message = errors });
            TempData["StatusMessage"] = "Password reset failed: " + errors;
            return RedirectToAction(nameof(Details), new { id });
        }

        await _auditService.LogAction(
            "ResetPassword",
            $"Admin reset the password for {user.Email}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (IsAjaxRequest())
        {
            return Json(new { success = true, email = user.Email, temporaryPassword });
        }

        TempData["StatusMessage"] = $"Password for {user.Email} was reset. Temporary password: {temporaryPassword}";
        return RedirectToAction(nameof(Details), new { id });
    }

    private string? CurrentAdminId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private bool IsAjaxRequest() => Request.Headers.TryGetValue("X-Requested-With", out var value) &&
        string.Equals(value.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*";
        const string all = upper + lower + digits + symbols;
        Span<char> password = stackalloc char[12];
        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (var i = 4; i < password.Length; i++) password[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        for (var i = password.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }
        return new string(password);
    }
}
