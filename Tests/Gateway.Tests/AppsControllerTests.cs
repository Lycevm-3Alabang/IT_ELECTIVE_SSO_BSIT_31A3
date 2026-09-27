using Data;
using Gateway.Controllers;
using Gateway.Models.Admin;
using Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;

namespace Gateway.Tests;

public class AppsControllerTests
{
    private static ServiceProvider BuildServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();

        services.AddDbContext<SsoDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        // AppsController doesn't use Identity directly, but SsoDbContext is
        // an IdentityDbContext, so its scoped services need to be registered
        // for the model to build (mirrors UsersControllerTests' setup).
        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<SsoDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IAuditService, AuditService>();

        return services.BuildServiceProvider();
    }

    private static AppsController BuildController(IServiceProvider provider)
    {
        var db = provider.GetRequiredService<SsoDbContext>();
        var auditService = provider.GetRequiredService<IAuditService>();

        var controller = new AppsController(db, auditService);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    // --- Issue #114: UI Test - Create App Flow -----------------------------

    [Fact]
    public async Task Create_WithValidModel_RegistersAppAsActive()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithValidModel_RegistersAppAsActive));
        var controller = BuildController(provider);

        var model = new CreateAppViewModel
        {
            Name = "HR Portal",
            ReturnUrl = "https://hr.example.com/callback"
        };

        var result = await controller.Create(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AppsController.Index), redirect.ActionName);

        var db = provider.GetRequiredService<SsoDbContext>();
        var created = await db.TenantApps.SingleOrDefaultAsync(a => a.Name == "HR Portal");

        Assert.NotNull(created);
        Assert.Equal("https://hr.example.com/callback", created!.ReturnUrl);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Create_WithInvalidModelState_DoesNotCreateApp()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithInvalidModelState_DoesNotCreateApp));
        var controller = BuildController(provider);
        controller.ModelState.AddModelError(nameof(CreateAppViewModel.Name), "App name is required.");

        var model = new CreateAppViewModel { Name = string.Empty, ReturnUrl = "https://example.com/callback" };

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);

        var db = provider.GetRequiredService<SsoDbContext>();
        Assert.Empty(db.TenantApps.ToList());
    }

    [Fact]
    public async Task Create_WithDuplicateName_ReturnsViewWithModelError_AndDoesNotCreateSecondApp()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithDuplicateName_ReturnsViewWithModelError_AndDoesNotCreateSecondApp));
        var controller = BuildController(provider);

        const string name = "Payroll System";

        var firstResult = await controller.Create(new CreateAppViewModel
        {
            Name = name,
            ReturnUrl = "https://payroll.example.com/callback"
        });
        Assert.IsType<RedirectToActionResult>(firstResult);

        var secondResult = await controller.Create(new CreateAppViewModel
        {
            Name = name,
            ReturnUrl = "https://payroll-v2.example.com/callback"
        });

        Assert.IsType<ViewResult>(secondResult);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(CreateAppViewModel.Name)));

        var db = provider.GetRequiredService<SsoDbContext>();
        Assert.Equal(1, await db.TenantApps.CountAsync(a => a.Name == name));
    }

    [Fact]
    public async Task Create_WithDuplicateName_DifferentCase_IsRejected()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithDuplicateName_DifferentCase_IsRejected));
        var controller = BuildController(provider);

        await controller.Create(new CreateAppViewModel
        {
            Name = "Intranet",
            ReturnUrl = "https://intranet.example.com/callback"
        });

        var result = await controller.Create(new CreateAppViewModel
        {
            Name = "INTRANET",
            ReturnUrl = "https://intranet2.example.com/callback"
        });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(CreateAppViewModel.Name)));
    }

    // --- Issue #115: UI Test - Edit App Flow --------------------------------

    [Fact]
    public async Task Edit_WithValidModel_UpdatesNameAndReturnUrl()
    {
        await using var provider = BuildServiceProvider(nameof(Edit_WithValidModel_UpdatesNameAndReturnUrl));
        var controller = BuildController(provider);

        await controller.Create(new CreateAppViewModel
        {
            Name = "Old Name",
            ReturnUrl = "https://old.example.com/callback"
        });

        var db = provider.GetRequiredService<SsoDbContext>();
        var app = await db.TenantApps.SingleAsync(a => a.Name == "Old Name");

        var result = await controller.Edit(app.Id, new EditAppViewModel
        {
            Id = app.Id,
            Name = "New Name",
            ReturnUrl = "https://new.example.com/callback"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AppsController.Index), redirect.ActionName);

        var reloaded = await db.TenantApps.FindAsync(app.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("New Name", reloaded!.Name);
        Assert.Equal("https://new.example.com/callback", reloaded.ReturnUrl);
    }

    [Fact]
    public async Task Edit_WithUnknownId_ReturnsNotFound()
    {
        await using var provider = BuildServiceProvider(nameof(Edit_WithUnknownId_ReturnsNotFound));
        var controller = BuildController(provider);

        var result = await controller.Edit(999, new EditAppViewModel
        {
            Id = 999,
            Name = "Ghost App",
            ReturnUrl = "https://ghost.example.com/callback"
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_WithMismatchedRouteAndModelId_ReturnsNotFound()
    {
        await using var provider = BuildServiceProvider(nameof(Edit_WithMismatchedRouteAndModelId_ReturnsNotFound));
        var controller = BuildController(provider);

        await controller.Create(new CreateAppViewModel
        {
            Name = "Some App",
            ReturnUrl = "https://some.example.com/callback"
        });

        var db = provider.GetRequiredService<SsoDbContext>();
        var app = await db.TenantApps.SingleAsync(a => a.Name == "Some App");

        var result = await controller.Edit(app.Id, new EditAppViewModel
        {
            Id = app.Id + 1,
            Name = "Some App",
            ReturnUrl = "https://some.example.com/callback"
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Edit_WithDuplicateName_ReturnsViewWithModelError_AndDoesNotOverwrite()
    {
        await using var provider = BuildServiceProvider(nameof(Edit_WithDuplicateName_ReturnsViewWithModelError_AndDoesNotOverwrite));
        var controller = BuildController(provider);

        await controller.Create(new CreateAppViewModel { Name = "App A", ReturnUrl = "https://a.example.com/callback" });
        await controller.Create(new CreateAppViewModel { Name = "App B", ReturnUrl = "https://b.example.com/callback" });

        var db = provider.GetRequiredService<SsoDbContext>();
        var appB = await db.TenantApps.SingleAsync(a => a.Name == "App B");

        var result = await controller.Edit(appB.Id, new EditAppViewModel
        {
            Id = appB.Id,
            Name = "App A",
            ReturnUrl = "https://b.example.com/callback"
        });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(EditAppViewModel.Name)));

        var reloaded = await db.TenantApps.FindAsync(appB.Id);
        Assert.Equal("App B", reloaded!.Name);
    }

    [Fact]
    public async Task Edit_WithInvalidModelState_DoesNotUpdateApp()
    {
        await using var provider = BuildServiceProvider(nameof(Edit_WithInvalidModelState_DoesNotUpdateApp));
        var controller = BuildController(provider);

        await controller.Create(new CreateAppViewModel { Name = "Keep Me", ReturnUrl = "https://keep.example.com/callback" });
        var db = provider.GetRequiredService<SsoDbContext>();
        var app = await db.TenantApps.SingleAsync(a => a.Name == "Keep Me");

        controller.ModelState.AddModelError(nameof(EditAppViewModel.ReturnUrl), "Enter a valid URL.");

        var result = await controller.Edit(app.Id, new EditAppViewModel
        {
            Id = app.Id,
            Name = "Keep Me",
            ReturnUrl = "not-a-url"
        });

        Assert.IsType<ViewResult>(result);
        var reloaded = await db.TenantApps.FindAsync(app.Id);
        Assert.Equal("https://keep.example.com/callback", reloaded!.ReturnUrl);
    }

    // --- Supporting coverage: toggle, delete, list --------------------------

    [Fact]
    public async Task ToggleActive_FlipsStatus_AndAjaxRequestReturnsJson()
    {
        await using var provider = BuildServiceProvider(nameof(ToggleActive_FlipsStatus_AndAjaxRequestReturnsJson));
        var controller = BuildController(provider);
        controller.HttpContext.Request.Headers.XRequestedWith = "XMLHttpRequest";

        await controller.Create(new CreateAppViewModel { Name = "Toggle App", ReturnUrl = "https://toggle.example.com/callback" });
        var db = provider.GetRequiredService<SsoDbContext>();
        var app = await db.TenantApps.SingleAsync(a => a.Name == "Toggle App");

        var result = await controller.ToggleActive(app.Id);

        var json = Assert.IsType<JsonResult>(result);
        var valueType = json.Value!.GetType();
        Assert.False((bool)valueType.GetProperty("isActive")!.GetValue(json.Value)!);
        Assert.Equal("Disabled", valueType.GetProperty("status")!.GetValue(json.Value));

        var reloaded = await db.TenantApps.FindAsync(app.Id);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task Delete_RemovesAppPermanently()
    {
        await using var provider = BuildServiceProvider(nameof(Delete_RemovesAppPermanently));
        var controller = BuildController(provider);

        await controller.Create(new CreateAppViewModel { Name = "Doomed App", ReturnUrl = "https://doomed.example.com/callback" });
        var db = provider.GetRequiredService<SsoDbContext>();
        var app = await db.TenantApps.SingleAsync(a => a.Name == "Doomed App");

        var result = await controller.Delete(app.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(await db.TenantApps.FindAsync(app.Id));
    }

    [Fact]
    public async Task Index_ReturnsAppsOrderedByName()
    {
        await using var provider = BuildServiceProvider(nameof(Index_ReturnsAppsOrderedByName));
        var controller = BuildController(provider);

        await controller.Create(new CreateAppViewModel { Name = "Zeta", ReturnUrl = "https://zeta.example.com/callback" });
        await controller.Create(new CreateAppViewModel { Name = "Alpha", ReturnUrl = "https://alpha.example.com/callback" });

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AppListViewModel>(view.Model);

        Assert.Equal(2, model.Apps.Count);
        Assert.Equal("Alpha", model.Apps[0].Name);
        Assert.Equal("Zeta", model.Apps[1].Name);
    }
}
