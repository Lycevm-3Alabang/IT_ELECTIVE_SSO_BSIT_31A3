using System.Security.Claims;
using Data;
using Gateway.Areas.Admin.Models.Groups;
using Gateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Areas.Admin.Controllers;

/// <summary>
/// Admin CRUD for app-scoped groups.
///
///   GET  /Admin/Groups               list all groups
///   GET  /Admin/Groups/Create        create form (app dropdown)
///   POST /Admin/Groups/Create        save; name is stored as [AppName]-[GroupName]
///   GET  /Admin/Groups/Edit/{id}     edit form
///   POST /Admin/Groups/Edit/{id}     update
///   POST /Admin/Groups/Delete/{id}   remove
///
/// Power level: 0 is the highest power, higher numbers mean less power.
/// Group names are unique per app (case-insensitive).
/// </summary>
public class GroupsController : AdminBaseController
{
    private readonly SsoDbContext _db;
    private readonly IAuditService _auditService;

    public GroupsController(SsoDbContext db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var groups = await _db.Groups.AsNoTracking()
            .OrderBy(g => g.TenantApp.Name)
            .ThenBy(g => g.PowerLevel)
            .ThenBy(g => g.Name)
            .Select(g => new GroupListItemViewModel
            {
                Id = g.Id,
                AppName = g.TenantApp.Name,
                Name = g.Name,
                PowerLevel = g.PowerLevel,
                MemberCount = _db.UserGroups.Count(ug => ug.GroupId == g.Id),
                CreatedAt = g.CreatedAt
            })
            .ToListAsync();

        return View(groups);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new GroupFormViewModel();
        await PopulateAppsAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GroupFormViewModel model)
    {
        TenantApp? app = null;
        if (model.TenantAppId is int appId)
        {
            app = await _db.TenantApps.FindAsync(appId);
        }

        if (app is null)
        {
            AddErrorOnce(nameof(model.TenantAppId), "Select the app this group belongs to.");
        }

        var fullName = await ValidateAsync(model, app, ignoreGroupId: 0);
        if (!ModelState.IsValid || app is null || fullName is null)
        {
            await PopulateAppsAsync(model);
            return View(model);
        }

        var level = model.PowerLevel!.Value;
        var group = new Group
        {
            TenantAppId = app.Id,
            Name = fullName,
            PowerLevel = level,
            CreatedAt = DateTime.UtcNow
        };

        _db.Groups.Add(group);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another admin may have created the same group between our
            // uniqueness check and the insert (unique index on TenantAppId + Name).
            _db.Entry(group).State = EntityState.Detached;
            if (!await NameExistsAsync(app.Id, fullName, ignoreGroupId: 0))
            {
                throw;
            }

            ModelState.AddModelError(nameof(model.GroupName), DuplicateMessage(fullName, app.Name));
            await PopulateAppsAsync(model);
            return View(model);
        }

        await _auditService.LogAction(
            "CreateGroup",
            $"Admin created group '{fullName}' with power level {level}.",
            CurrentAdminId(),
            ClientIp());

        TempData["StatusMessage"] = $"Group '{fullName}' was created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var group = await _db.Groups.AsNoTracking()
            .Include(g => g.TenantApp)
            .FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();

        return View(new GroupFormViewModel
        {
            Id = group.Id,
            TenantAppId = group.TenantAppId,
            AppName = group.TenantApp.Name,
            GroupName = GroupRules.ShortName(group.TenantApp.Name, group.Name),
            PowerLevel = group.PowerLevel
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, GroupFormViewModel model)
    {
        var group = await _db.Groups
            .Include(g => g.TenantApp)
            .FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();

        // A group stays with its app: moving it would silently change what its
        // members are allowed to do. Whatever app id was posted is ignored.
        var app = group.TenantApp;
        ModelState.Remove(nameof(model.TenantAppId)); // not used on Edit; drop its binder error
        model.Id = id;
        model.TenantAppId = group.TenantAppId;
        model.AppName = app.Name;

        var fullName = await ValidateAsync(model, app, ignoreGroupId: id);
        if (!ModelState.IsValid || fullName is null)
        {
            return View(model);
        }

        var level = model.PowerLevel!.Value;
        group.Name = fullName;
        group.PowerLevel = level;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (!await NameExistsAsync(app.Id, fullName, ignoreGroupId: id))
            {
                throw;
            }

            ModelState.AddModelError(nameof(model.GroupName), DuplicateMessage(fullName, app.Name));
            return View(model);
        }

        await _auditService.LogAction(
            "UpdateGroup",
            $"Admin updated group '{fullName}' (power level {level}).",
            CurrentAdminId(),
            ClientIp());

        TempData["StatusMessage"] = $"Group '{fullName}' was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var group = await _db.Groups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();

        // Drop memberships explicitly so this behaves the same on every EF
        // provider (the database also cascades, the in-memory provider doesn't).
        var memberships = await _db.UserGroups.Where(ug => ug.GroupId == id).ToListAsync();
        _db.UserGroups.RemoveRange(memberships);
        _db.Groups.Remove(group);
        await _db.SaveChangesAsync();

        await _auditService.LogAction(
            "DeleteGroup",
            $"Admin deleted group '{group.Name}' (power level {group.PowerLevel}, {memberships.Count} member(s) removed).",
            CurrentAdminId(),
            ClientIp());

        TempData["StatusMessage"] = $"Group '{group.Name}' was deleted.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Validates the power level and the name, adding errors to ModelState.
    /// Returns the full stored name ("[AppName]-[GroupName]") when the name is
    /// usable, otherwise null. Runs even when the model binder already
    /// validated the attributes, so the rules also hold when the action is
    /// called directly (unit tests, other callers).
    /// </summary>
    private async Task<string?> ValidateAsync(GroupFormViewModel model, TenantApp? app, int ignoreGroupId)
    {
        if (model.PowerLevel is not int level)
        {
            AddErrorOnce(nameof(model.PowerLevel), "Power level is required.");
        }
        else if (!GroupRules.IsValidPowerLevel(level))
        {
            AddErrorOnce(
                nameof(model.PowerLevel),
                $"Power level must be a whole number from {GroupRules.MinPowerLevel} (highest power) " +
                $"to {GroupRules.MaxPowerLevel} (lowest power).");
        }

        if (app is null)
        {
            return null; // can't build the prefix without an app
        }

        var shortName = GroupRules.ShortName(app.Name, model.GroupName);
        if (shortName.Length == 0)
        {
            AddErrorOnce(nameof(model.GroupName), "Group name is required.");
            return null;
        }

        var fullName = GroupRules.BuildName(app.Name, shortName);
        if (fullName.Length > GroupRules.MaxNameLength)
        {
            AddErrorOnce(
                nameof(model.GroupName),
                $"'{fullName}' is {fullName.Length} characters. The app prefix counts too, " +
                $"so the full name can be at most {GroupRules.MaxNameLength}.");
            return null;
        }

        if (await NameExistsAsync(app.Id, fullName, ignoreGroupId))
        {
            AddErrorOnce(nameof(model.GroupName), DuplicateMessage(fullName, app.Name));
            return null;
        }

        return fullName;
    }

    private Task<bool> NameExistsAsync(int appId, string fullName, int ignoreGroupId)
    {
        var lowered = fullName.ToLower();
        return _db.Groups.AnyAsync(g =>
            g.TenantAppId == appId &&
            g.Id != ignoreGroupId &&
            g.Name.ToLower() == lowered);
    }

    private static string DuplicateMessage(string fullName, string appName) =>
        $"A group named '{fullName}' already exists for {appName}.";

    private async Task PopulateAppsAsync(GroupFormViewModel model)
    {
        model.Apps = await _db.TenantApps.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new AppOption(a.Id, a.Name, a.IsEnabled))
            .ToListAsync();
    }

    /// <summary>
    /// Adds an error unless the field already has one, so a failure caught by
    /// both the model binder's attributes and the checks above shows once.
    /// </summary>
    private void AddErrorOnce(string key, string message)
    {
        if (ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0)
        {
            return;
        }

        ModelState.AddModelError(key, message);
    }

    private string? CurrentAdminId() => HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    private string? ClientIp() => HttpContext?.Connection?.RemoteIpAddress?.ToString();
}
