using System.Security.Claims;
using Data;
using Gateway.Services;
using Gateway.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Controllers;

[Authorize(Roles = SeedData.AdminRole)]
[Route("Admin/Users")]
public class UsersController : Controller
{
    private const int MaxPageSize = 100;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;

    public UsersController(UserManager<ApplicationUser> userManager, IAuditService auditService)
    {
        _userManager = userManager;
        _auditService = auditService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, int pageSize = 10)
    {
        if (page < 1)
        {
            page = 1;
        }

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        IQueryable<ApplicationUser> query = _userManager.Users.OrderBy(u => u.Email);

        var totalCount = await query.CountAsync();

        var users = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserListItemViewModel
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
            })
            .ToListAsync();

        var model = new UserListViewModel
        {
            Users = users,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };

        return View(model);
    }

    [HttpGet("Create")]
    public IActionResult Create()
    {
        return View(new CreateUserViewModel());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await _userManager.FindByEmailAsync(model.Email);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "A user with this email already exists.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await _userManager.CreateAsync(user, model.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await _auditService.LogAction(
            "CreateUser",
            $"Admin created user {user.Email}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["StatusMessage"] = $"User {user.Email} was created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Details/{id}")]
    public async Task<IActionResult> Details(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);

        var model = new UserDetailsViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Roles = roles.ToList(),
        };

        return View(model);
    }


    [HttpPost("ToggleActive/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = !user.IsActive;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));

            if (IsAjaxRequest())
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = errors
                });
            }

            TempData["StatusMessage"] = $"Failed to change the status of {user.Email}: {errors}";
            return RedirectToAction(nameof(Index));
        }

        await _auditService.LogAction(
            "ToggleActive",
            $"Admin changed {user.Email} to {(user.IsActive ? "active" : "inactive")}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (IsAjaxRequest())
        {
            return Json(new
            {
                success = true,
                id = user.Id,
                isActive = user.IsActive,
                status = user.IsActive ? "Active" : "Inactive"
            });
        }

        TempData["StatusMessage"] = $"User {user.Email} is now {(user.IsActive ? "active" : "inactive")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("ResetPassword/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, string password, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6 || password != confirmPassword)
        {
            TempData["StatusMessage"] = "Password reset failed. Use a matching password with at least 6 characters.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = "Password reset failed: " + string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Details), new { id });
        }

        await _auditService.LogAction(
            "ResetPassword",
            $"Admin reset the password for {user.Email}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["StatusMessage"] = $"Password for {user.Email} was reset.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private string? CurrentAdminId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private bool IsAjaxRequest() =>
        Request.Headers.TryGetValue("X-Requested-With", out var value) &&
        string.Equals(value.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    [HttpPost("Delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = false;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            TempData["StatusMessage"] = $"Failed to deactivate {user.Email}: {errors}";
            return RedirectToAction(nameof(Index));
        }

        await _auditService.LogAction(
            "DeactivateUser",
            $"Admin deactivated {user.Email}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["StatusMessage"] = $"User {user.Email} was deactivated.";
        return RedirectToAction(nameof(Index));
    }
}
