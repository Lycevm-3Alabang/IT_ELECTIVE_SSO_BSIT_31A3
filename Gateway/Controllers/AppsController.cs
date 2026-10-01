using Data;
using Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Controllers;

[Area("Admin")]
[Authorize(Roles = SeedData.AdminRole)]
[Route("Admin/TenantApps")]
public class TenantAppsController : Controller
{
    private readonly SsoDbContext _db;

    public TenantAppsController(SsoDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var apps = await _db.TenantApps.AsNoTracking().OrderBy(a => a.Name).ToListAsync();
        return View("~/Views/Apps/Index.cshtml", apps.Select(ToViewModel));
    }

    [HttpGet("Create")]
    public IActionResult Create() => View("~/Views/Apps/Create.cshtml", new ExternalApp());

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ExternalApp model)
    {
        NormalizeAndValidateUrl(model);
        if (await _db.TenantApps.AnyAsync(a => a.Name.ToLower() == model.Name.Trim().ToLower()))
            ModelState.AddModelError(nameof(model.Name), "An app with this name already exists.");

        if (!ModelState.IsValid) return View("~/Views/Apps/Create.cshtml", model);

        var entity = new TenantApp
        {
            Name = model.Name.Trim(),
            ReturnUrl = model.ReturnUrl.Trim(),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.TenantApps.Add(entity);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var entity = await _db.TenantApps.FindAsync(id);
        if (entity is null) return NotFound();
        return View("~/Views/Apps/Edit.cshtml", ToViewModel(entity));
    }

    [HttpPost("Edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ExternalApp model)
    {
        var entity = await _db.TenantApps.FindAsync(id);
        if (entity is null) return NotFound();

        NormalizeAndValidateUrl(model);
        if (await _db.TenantApps.AnyAsync(a => a.Id != id && a.Name.ToLower() == model.Name.Trim().ToLower()))
            ModelState.AddModelError(nameof(model.Name), "An app with this name already exists.");

        if (!ModelState.IsValid)
        {
            model.Id = id;
            model.IsEnabled = entity.IsEnabled;
            model.CreatedAt = entity.CreatedAt;
            return View("~/Views/Apps/Edit.cshtml", model);
        }

        entity.Name = model.Name.Trim();
        entity.ReturnUrl = model.ReturnUrl.Trim();
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Delete/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.TenantApps.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (entity is null) return NotFound();
        return View("~/Views/Apps/Delete.cshtml", ToViewModel(entity));
    }

    [HttpPost("Delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var entity = await _db.TenantApps.FindAsync(id);
        if (entity is null) return NotFound();
        _db.TenantApps.Remove(entity);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Toggle/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var entity = await _db.TenantApps.FindAsync(id);
        if (entity is null) return NotFound();
        entity.IsEnabled = !entity.IsEnabled;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private static ExternalApp ToViewModel(TenantApp entity) => new()
    {
        Id = entity.Id, Name = entity.Name, ReturnUrl = entity.ReturnUrl ?? string.Empty,
        IsEnabled = entity.IsEnabled, CreatedAt = entity.CreatedAt
    };

    private void NormalizeAndValidateUrl(ExternalApp model)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        model.ReturnUrl = model.ReturnUrl?.Trim() ?? string.Empty;

        if (!Uri.TryCreate(model.ReturnUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            ModelState.AddModelError(nameof(model.ReturnUrl), "Return URL must be an absolute HTTP or HTTPS URL.");
        }
    }
}
