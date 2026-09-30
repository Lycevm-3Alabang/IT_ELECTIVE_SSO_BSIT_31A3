using Data;
using Gateway.Controllers;
using Gateway.Models.Admin;
using Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;

namespace Gateway.Tests;

public class GroupsControllerTests
{
    private static ServiceProvider BuildServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<SsoDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddScoped<IAuditService, AuditService>();

        return services.BuildServiceProvider();
    }

    private static GroupsController BuildController(IServiceProvider provider)
    {
        var db = provider.GetRequiredService<SsoDbContext>();
        var auditService = provider.GetRequiredService<IAuditService>();

        return new GroupsController(db, auditService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static async Task<TenantApp> AddTenantAppAsync(SsoDbContext db, string name = "SalesApp")
    {
        var app = new TenantApp
        {
            Name = name,
            ReturnUrl = $"https://{name.ToLower()}.example.com/callback",
            IsEnabled = true,
        };

        db.TenantApps.Add(app);
        await db.SaveChangesAsync();
        return app;
    }

    [Fact]
    public async Task Create_WithValidModel_AutoPrefixesGroupNameWithAppName()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithValidModel_AutoPrefixesGroupNameWithAppName));
        var db = provider.GetRequiredService<SsoDbContext>();
        var app = await AddTenantAppAsync(db);

        var controller = BuildController(provider);
        var model = new CreateGroupViewModel
        {
            TenantAppId = app.Id,
            GroupName = "Admin",
            PowerLevel = 0,
        };

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);

        var group = await db.Groups.SingleAsync();
        Assert.Equal("SalesApp-Admin", group.Name);
        Assert.Equal(app.Id, group.TenantAppId);
        Assert.Equal(0, group.PowerLevel);
    }

    [Fact]
    public async Task Create_WithDuplicateNameForSameApp_ReturnsValidationError()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithDuplicateNameForSameApp_ReturnsValidationError));
        var db = provider.GetRequiredService<SsoDbContext>();
        var app = await AddTenantAppAsync(db);

        db.Groups.Add(new Group { TenantAppId = app.Id, Name = "SalesApp-Admin", PowerLevel = 0 });
        await db.SaveChangesAsync();

        var controller = BuildController(provider);
        var model = new CreateGroupViewModel
        {
            TenantAppId = app.Id,
            GroupName = "Admin",
            PowerLevel = 1,
        };

        var result = await controller.Create(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(CreateGroupViewModel.GroupName)));
        Assert.Equal(1, await db.Groups.CountAsync());
    }

    [Fact]
    public async Task Create_WithDuplicateNameForDifferentApp_Succeeds()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithDuplicateNameForDifferentApp_Succeeds));
        var db = provider.GetRequiredService<SsoDbContext>();
        var salesApp = await AddTenantAppAsync(db, "SalesApp");
        var hrApp = await AddTenantAppAsync(db, "HrApp");

        db.Groups.Add(new Group { TenantAppId = salesApp.Id, Name = "SalesApp-Admin", PowerLevel = 0 });
        await db.SaveChangesAsync();

        var controller = BuildController(provider);
        var model = new CreateGroupViewModel
        {
            TenantAppId = hrApp.Id,
            GroupName = "Admin",
            PowerLevel = 0,
        };

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(2, await db.Groups.CountAsync());
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void PowerLevel_ValidationAttribute_EnforcesZeroToOneHundredRange(int level, bool expectedValid)
    {
        var model = new CreateGroupViewModel
        {
            TenantAppId = 1,
            GroupName = "Manager",
            PowerLevel = level,
        };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(model);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            model, context, results, validateAllProperties: true);

        var levelIsValid = !results.Exists(r =>
            r.MemberNames.Contains(nameof(CreateGroupViewModel.PowerLevel)));

        Assert.Equal(expectedValid, isValid && levelIsValid);
    }

    [Fact]
    public async Task Edit_ChangingLevel_UpdatesGroupWithoutRenaming()
    {
        await using var provider = BuildServiceProvider(nameof(Edit_ChangingLevel_UpdatesGroupWithoutRenaming));
        var db = provider.GetRequiredService<SsoDbContext>();
        var app = await AddTenantAppAsync(db);

        var group = new Group { TenantAppId = app.Id, Name = "SalesApp-Manager", PowerLevel = 1 };
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var controller = BuildController(provider);
        var model = new EditGroupViewModel
        {
            Id = group.Id,
            TenantAppId = app.Id,
            GroupName = "Manager",
            PowerLevel = 2,
        };

        var result = await controller.Edit(group.Id, model);

        Assert.IsType<RedirectToActionResult>(result);

        var updated = await db.Groups.FindAsync(group.Id);
        Assert.NotNull(updated);
        Assert.Equal("SalesApp-Manager", updated!.Name);
        Assert.Equal(2, updated.PowerLevel);
    }

    [Fact]
    public void BuildPrefixedName_CombinesAppNameAndRawGroupName()
    {
        var result = GroupsController.BuildPrefixedName("SalesApp", "  Admin  ");
        Assert.Equal("SalesApp-Admin", result);
    }

    [Fact]
    public void StripPrefix_RemovesAppNamePrefix()
    {
        var result = GroupsController.StripPrefix("SalesApp", "SalesApp-Admin");
        Assert.Equal("Admin", result);
    }
}
