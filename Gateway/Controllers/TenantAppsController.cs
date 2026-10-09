using System.Security.Claims;
using Data;
using Gateway.Models;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Controllers;

/// <summary>
/// Admin screens for registering the client apps that may use this gateway to sign users in.
/// Apps are stored in the TenantApps table; the login gateway reads the same table.
///
///   GET  /Admin/TenantApps               list
///   GET  /Admin/TenantApps/Create        register form
///   POST /Admin/TenantApps/Create        save
///   GET  /Admin/TenantApps/Edit/{id}     edit form
///   POST /Admin/TenantApps/Edit/{id}     save (renaming an app renames the prefix of its groups)
///   POST /Admin/TenantApps/ToggleStatus/{id}
///   POST /Admin/TenantApps/Delete/{id}   also removes the app's groups
/// </summary>
[Area("Admin")]
[Authorize(Roles = SeedData.AdminRole)]
public class TenantAppsController : Controller
{
    private const string DuplicateNameMessage = "An application with this name already exists.";
    private const string DuplicateUrlMessage = "Another application already uses this return URL.";

    private readonly SsoDbContext _db;
    private readonly IAuditService? _audit;

    public TenantAppsController(SsoDbContext db)
    {
        _db = db;
    }

    // The container uses this constructor, so admin changes land in the audit log.
    [ActivatorUtilitiesConstructor]
    public TenantAppsController(SsoDbContext db, IAuditService audit) : this(db)
    {
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var apps = await _db.TenantApps.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ExternalApp
            {
                Id = a.Id,
                Name = a.Name,
                ReturnUrl = a.ReturnUrl,
                IsEnabled = a.IsEnabled
            })
            .ToListAsync();

        return View(apps);
    }

    [HttpGet]
    public IActionResult Create() => View(new ExternalApp());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ExternalApp app)
    {
        Normalize(app);
        await ValidateAsync(app, ignoreId: 0);

        if (!ModelState.IsValid)
        {
            return View(app);
        }

        var entity = new TenantApp
        {
            Name = app.Name,
            ReturnUrl = app.ReturnUrl,
            IsEnabled = app.IsEnabled,
            CreatedAt = DateTime.UtcNow
        };
        _db.TenantApps.Add(entity);

        if (!await TrySaveAsync())
        {
            _db.Entry(entity).State = EntityState.Detached;
            ModelState.AddModelError(nameof(ExternalApp.Name), DuplicateNameMessage);
            return View(app);
        }

        await LogAsync("TenantAppCreated", $"Admin registered app '{entity.Name}' (Id {entity.Id}).");
        Flash($"Application '{entity.Name}' was registered.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var entity = await _db.TenantApps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (entity is null)
        {
            return NotFound();
        }

        return View(new ExternalApp
        {
            Id = entity.Id,
            Name = entity.Name,
            ReturnUrl = entity.ReturnUrl,
            IsEnabled = entity.IsEnabled
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ExternalApp app)
    {
        var entity = await _db.TenantApps.Include(a => a.Groups).FirstOrDefaultAsync(a => a.Id == id);
        if (entity is null)
        {
            return NotFound();
        }

        Normalize(app);
        app.Id = id;
        await ValidateAsync(app, ignoreId: id);

        if (!ModelState.IsValid)
        {
            return View(app);
        }

        var oldName = entity.Name;
        entity.Name = app.Name;
        entity.ReturnUrl = app.ReturnUrl;
        entity.IsEnabled = app.IsEnabled;

        // Groups are stored as "[AppName]-[GroupName]", so a rename has to carry over to them.
        if (!string.Equals(oldName, app.Name, StringComparison.Ordinal))
        {
            foreach (var group in entity.Groups)
            {
                group.Name = GroupRules.ReplacePrefix(oldName, app.Name, group.Name);
            }
        }

        if (!await TrySaveAsync())
        {
            ModelState.AddModelError(nameof(ExternalApp.Name), DuplicateNameMessage);
            return View(app);
        }

        await LogAsync("TenantAppUpdated", $"Admin updated app '{entity.Name}' (Id {entity.Id}).");
        Flash($"Application '{entity.Name}' was updated.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var entity = await _db.TenantApps.FirstOrDefaultAsync(a => a.Id == id);
        if (entity is null)
        {
            return NotFound();
        }

        entity.IsEnabled = !entity.IsEnabled;
        await _db.SaveChangesAsync();

        var state = entity.IsEnabled ? "enabled" : "disabled";
        await LogAsync(entity.IsEnabled ? "TenantAppEnabled" : "TenantAppDisabled",
            $"Admin {state} app '{entity.Name}' (Id {entity.Id}).");
        Flash($"Application '{entity.Name}' was {state}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.TenantApps.FirstOrDefaultAsync(a => a.Id == id);
        if (entity is null)
        {
            return NotFound();
        }

        var name = entity.Name;
        _db.TenantApps.Remove(entity); // its groups cascade; audit-log rows keep their text but lose the link
        await _db.SaveChangesAsync();

        await LogAsync("TenantAppDeleted", $"Admin deleted app '{name}' (Id {id}).");
        Flash($"Application '{name}' was deleted.");
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ helpers

    private static void Normalize(ExternalApp app)
    {
        app.Name = (app.Name ?? string.Empty).Trim();
        app.ReturnUrl = (app.ReturnUrl ?? string.Empty).Trim();
    }

    private async Task ValidateAsync(ExternalApp app, int ignoreId)
    {
        if (string.IsNullOrWhiteSpace(app.Name))
        {
            AddErrorOnce(nameof(ExternalApp.Name), "App name is required.");
        }
        else if (app.Name.Length > 100)
        {
            AddErrorOnce(nameof(ExternalApp.Name), "App name must be 100 characters or fewer.");
        }
        else
        {
            var lowered = app.Name.ToLower();
            var taken = await _db.TenantApps.AnyAsync(a => a.Id != ignoreId && a.Name.ToLower() == lowered);
            if (taken)
            {
                AddErrorOnce(nameof(ExternalApp.Name), DuplicateNameMessage);
            }
        }

        if (string.IsNullOrWhiteSpace(app.ReturnUrl))
        {
            AddErrorOnce(nameof(ExternalApp.ReturnUrl), "Return URL is required.");
        }
        else if (app.ReturnUrl.Length > 500)
        {
            AddErrorOnce(nameof(ExternalApp.ReturnUrl), "Return URL must be 500 characters or fewer.");
        }
        else if (!IsAcceptableReturnUrl(app.ReturnUrl))
        {
            // The same shape the login gateway insists on: absolute http(s), no credentials or fragment, no "token" parameter.
            AddErrorOnce(nameof(ExternalApp.ReturnUrl),
                "Enter a full http:// or https:// address, without a login, a # fragment or a 'token' parameter.");
        }
        else
        {
            var lowered = app.ReturnUrl.ToLower();
            var taken = await _db.TenantApps.AnyAsync(a => a.Id != ignoreId && a.ReturnUrl.ToLower() == lowered);
            if (taken)
            {
                AddErrorOnce(nameof(ExternalApp.ReturnUrl), DuplicateUrlMessage);
            }
        }
    }

    private static bool IsAcceptableReturnUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
        {
            return false;
        }

        return !Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).ContainsKey("token");
    }

    private void AddErrorOnce(string key, string message)
    {
        if (!ModelState.TryGetValue(key, out var entry) || entry.Errors.All(e => e.ErrorMessage != message))
        {
            ModelState.AddModelError(key, message);
        }
    }

    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // The unique index on Name is the last line of defence against two admins saving the same name at once.
            return false;
        }
    }

    private void Flash(string message)
    {
        if (HttpContext is not null)
        {
            TempData["StatusMessage"] = message;
        }
    }

    private async Task LogAsync(string action, string details)
    {
        if (_audit is null || HttpContext is null)
        {
            return;
        }

        await _audit.LogAction(
            action,
            details,
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            HttpContext.Connection.RemoteIpAddress?.ToString());
    }
}
