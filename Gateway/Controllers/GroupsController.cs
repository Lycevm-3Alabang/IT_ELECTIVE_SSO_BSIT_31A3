using System.Security.Claims;
using Data;
using Gateway.Models.Admin;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Controllers;

/// <summary>
/// Issue 10: lets the Main Admin create groups for a specific app with a
/// power level, so external apps can tell how much access a member should
/// get. Group names are auto-prefixed with the owning app's name
/// ("[AppName]-[GroupName]") so names stay unique per app and line up with
/// the "groups"/"levels" claims documented for the JWT.
/// </summary>
[Authorize(Roles = SeedData.AdminRole)]
[Route("Admin/Groups")]
public class GroupsController : Controller
{
    private readonly SsoDbContext _db;
    private readonly IAuditService _auditService;

    public GroupsController(SsoDbContext db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var groups = await _db.Groups
            .Include(g => g.TenantApp)
            .OrderBy(g => g.TenantApp.Name)
            .ThenBy(g => g.PowerLevel)
            .Select(g => new GroupListItemViewModel
            {
                Id = g.Id,
                Name = g.Name,
                AppName = g.TenantApp.Name,
                PowerLevel = g.PowerLevel,
                CreatedAt = g.CreatedAt,
            })
            .ToListAsync();

        return View(new GroupListViewModel { Groups = groups });
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create()
    {
        var model = new CreateGroupViewModel
        {
            TenantAppOptions = await GetTenantAppOptionsAsync(),
        };

        return View(model);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateGroupViewModel model)
    {
        var tenantApp = await _db.TenantApps.FindAsync(model.TenantAppId);
        if (tenantApp is null)
        {
            ModelState.AddModelError(nameof(model.TenantAppId), "Select a valid app.");
        }

        string? prefixedName = null;
        if (tenantApp is not null && !string.IsNullOrWhiteSpace(model.GroupName))
        {
            prefixedName = BuildPrefixedName(tenantApp.Name, model.GroupName);

            var isDuplicate = await _db.Groups.AnyAsync(g =>
                g.TenantAppId == model.TenantAppId &&
                g.Name.ToLower() == prefixedName.ToLower());

            if (isDuplicate)
            {
                ModelState.AddModelError(
                    nameof(model.GroupName),
                    "A group with this name already exists for the selected app.");
            }
        }

        if (!ModelState.IsValid)
        {
            model.TenantAppOptions = await GetTenantAppOptionsAsync();
            return View(model);
        }

        var group = new Group
        {
            TenantAppId = tenantApp!.Id,
            Name = prefixedName!,
            PowerLevel = model.PowerLevel,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Groups.Add(group);
        await _db.SaveChangesAsync();

        await _auditService.LogAction(
            "CreateGroup",
            $"Admin created group {group.Name} (level {group.PowerLevel}) for app {tenantApp.Name}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["StatusMessage"] = $"Group {group.Name} was created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var group = await _db.Groups
            .Include(g => g.TenantApp)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group is null)
        {
            return NotFound();
        }

        var model = new EditGroupViewModel
        {
            Id = group.Id,
            TenantAppId = group.TenantAppId,
            GroupName = StripPrefix(group.TenantApp.Name, group.Name),
            PowerLevel = group.PowerLevel,
            TenantAppOptions = await GetTenantAppOptionsAsync(),
        };

        return View(model);
    }

    [HttpPost("Edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditGroupViewModel model)
    {
        var group = await _db.Groups
            .Include(g => g.TenantApp)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group is null)
        {
            return NotFound();
        }

        var tenantApp = await _db.TenantApps.FindAsync(model.TenantAppId);
        if (tenantApp is null)
        {
            ModelState.AddModelError(nameof(model.TenantAppId), "Select a valid app.");
        }

        string? prefixedName = null;
        if (tenantApp is not null && !string.IsNullOrWhiteSpace(model.GroupName))
        {
            prefixedName = BuildPrefixedName(tenantApp.Name, model.GroupName);

            var isDuplicate = await _db.Groups.AnyAsync(g =>
                g.Id != id &&
                g.TenantAppId == model.TenantAppId &&
                g.Name.ToLower() == prefixedName.ToLower());

            if (isDuplicate)
            {
                ModelState.AddModelError(
                    nameof(model.GroupName),
                    "A group with this name already exists for the selected app.");
            }
        }

        if (!ModelState.IsValid)
        {
            model.TenantAppOptions = await GetTenantAppOptionsAsync();
            return View(model);
        }

        group.TenantAppId = tenantApp!.Id;
        group.Name = prefixedName!;
        group.PowerLevel = model.PowerLevel;

        await _db.SaveChangesAsync();

        await _auditService.LogAction(
            "UpdateGroup",
            $"Admin updated group {group.Name} (level {group.PowerLevel}).",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["StatusMessage"] = $"Group {group.Name} was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var group = await _db.Groups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is null)
        {
            return NotFound();
        }

        _db.Groups.Remove(group);
        await _db.SaveChangesAsync();

        await _auditService.LogAction(
            "DeleteGroup",
            $"Admin removed group {group.Name}.",
            CurrentAdminId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["StatusMessage"] = $"Group {group.Name} was removed.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<SelectListItem>> GetTenantAppOptionsAsync()
    {
        return await _db.TenantApps
            .OrderBy(a => a.Name)
            .Select(a => new SelectListItem { Value = a.Id.ToString(), Text = a.Name })
            .ToListAsync();
    }

    /// <summary>Builds the stored, fully-qualified group name from an app and a raw suffix.</summary>
    public static string BuildPrefixedName(string appName, string rawGroupName) =>
        $"{appName}-{rawGroupName.Trim()}";

    /// <summary>Reverses <see cref="BuildPrefixedName"/> for display on the edit form.</summary>
    public static string StripPrefix(string appName, string fullName)
    {
        var prefix = $"{appName}-";
        return fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fullName[prefix.Length..]
            : fullName;
    }

    private string? CurrentAdminId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
}
