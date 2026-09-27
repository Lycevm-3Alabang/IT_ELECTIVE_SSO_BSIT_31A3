using System.Security.Claims;
using Data;
using Gateway.Models.Admin;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Controllers;

// Issue 14 (#107): Tenant App Management UI.
// Follows the same shape as UsersController/AuditController: a top-level,
// attribute-routed "Admin/..." controller restricted to the Admin role,
// backed directly by SsoDbContext, with actions audited via IAuditService.
[Authorize(Roles = SeedData.AdminRole)]
[Route("Admin/Apps")]
public class AppsController : Controller
{
    private readonly SsoDbContext _db;
    private readonly IAuditService _auditService;

    public AppsController(SsoDbContext db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    // GET: /Admin/Apps
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var apps = await _db.TenantApps
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new AppListItemViewModel
            {
                Id = a.Id,
                Name = a.Name,
                ReturnUrl = a.ReturnUrl,
                IsActive = a.IsActive
            })
            .ToListAsync();

        return View(new AppListViewModel { Apps = apps });
    }

    // GET: /Admin/Apps/Create
    [HttpGet("Create")]
    public IActionResult Create() => View(new CreateAppViewModel());

    // POST: /Admin/Apps/Create
    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateAppViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (await NameExistsAsync(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "An app with this name already exists.");
            return View(model);
        }

        var app = new TenantApp
        {
            Name = model.Name,
            ReturnUrl = model.ReturnUrl,
            IsActive = true
        };

        _db.TenantApps.Add(app);
        await _db.SaveChangesAsync();

        await _auditService.LogAction(
            "CreateApp",
            $"Admin registered app {app.Name}.",
            CurrentAdminId(),
            RemoteIp());

        TempData["StatusMessage"] = $"App {app.Name} was registered.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Admin/Apps/Edit/1
    [HttpGet("Edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var app = await _db.TenantApps.FindAsync(id);
        if (app is null)
        {
            return NotFound();
        }

        return View(new EditAppViewModel
        {
            Id = app.Id,
            Name = app.Name,
            ReturnUrl = app.ReturnUrl
        });
    }

    // POST: /Admin/Apps/Edit/1
    [HttpPost("Edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditAppViewModel model)
    {
        if (id != model.Id)
        {
            return NotFound();
        }

        var app = await _db.TenantApps.FindAsync(id);
        if (app is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (await NameExistsAsync(model.Name, excludingId: id))
        {
            ModelState.AddModelError(nameof(model.Name), "An app with this name already exists.");
            return View(model);
        }

        app.Name = model.Name;
        app.ReturnUrl = model.ReturnUrl;
        await _db.SaveChangesAsync();

        await _auditService.LogAction(
            "EditApp",
            $"Admin updated app {app.Name}.",
            CurrentAdminId(),
            RemoteIp());

        TempData["StatusMessage"] = $"App {app.Name} was updated.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Admin/Apps/ToggleActive/1
    // AJAX-aware: the app list toggles apps in place via fetch(), the same
    // pattern UsersController.ToggleActive uses for the active switch.
    [HttpPost("ToggleActive/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var app = await _db.TenantApps.FindAsync(id);
        if (app is null)
        {
            return NotFound();
        }

        app.IsActive = !app.IsActive;
        await _db.SaveChangesAsync();

        await _auditService.LogAction(
            "ToggleApp",
            $"Admin set {app.Name} to {(app.IsActive ? "active" : "disabled")}.",
            CurrentAdminId(),
            RemoteIp());

        if (IsAjaxRequest())
        {
            return Json(new
            {
                success = true,
                id = app.Id,
                isActive = app.IsActive,
                status = app.IsActive ? "Active" : "Disabled"
            });
        }

        TempData["StatusMessage"] = $"App {app.Name} is now {(app.IsActive ? "active" : "disabled")}.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Admin/Apps/Delete/1
    // Unlike Users (soft-deactivate), Issue #111 asks for a *permanent*
    // delete behind a confirmation modal, so this removes the row outright.
    [HttpPost("Delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var app = await _db.TenantApps.FindAsync(id);
        if (app is null)
        {
            return NotFound();
        }

        var name = app.Name;
        _db.TenantApps.Remove(app);
        await _db.SaveChangesAsync();

        await _auditService.LogAction(
            "DeleteApp",
            $"Admin deleted app {name}.",
            CurrentAdminId(),
            RemoteIp());

        if (IsAjaxRequest())
        {
            return Json(new { success = true, id });
        }

        TempData["StatusMessage"] = $"App {name} was deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> NameExistsAsync(string name, int? excludingId = null)
    {
        var normalized = name.Trim().ToLower();

        return await _db.TenantApps.AnyAsync(a =>
            a.Name.ToLower() == normalized &&
            (excludingId == null || a.Id != excludingId));
    }

    private string? CurrentAdminId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private bool IsAjaxRequest() =>
        Request.Headers.TryGetValue("X-Requested-With", out var value) &&
        string.Equals(value.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
