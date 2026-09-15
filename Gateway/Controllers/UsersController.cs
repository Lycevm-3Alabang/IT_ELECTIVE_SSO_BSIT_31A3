using Data;
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

    public UsersController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
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

        TempData["StatusMessage"] = $"User {user.Email} was deactivated.";
        return RedirectToAction(nameof(Index));
    }
}
