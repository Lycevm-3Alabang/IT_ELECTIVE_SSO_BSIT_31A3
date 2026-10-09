using Data;
using Gateway.Areas.Admin.Controllers;
using Gateway.Areas.Admin.Models.Groups;
using Gateway.Controllers;
using Gateway.Models;
using Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;

namespace Gateway.Tests;

public class GroupsControllerTests
{
    // ---------------------------------------------------------------- helpers

    private static ServiceProvider BuildServiceProvider(string testName)
    {
        // Unique store per call: theory rows and tests never share data.
        var dbName = $"{testName}-{Guid.NewGuid()}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SsoDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddScoped<IAuditService, AuditService>();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<SsoDbContext>().Database.EnsureCreated();
        return provider;
    }

    private static GroupsController BuildController(IServiceProvider provider)
    {
        var httpContext = new DefaultHttpContext();
        return new GroupsController(
            provider.GetRequiredService<SsoDbContext>(),
            provider.GetRequiredService<IAuditService>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NullTempDataProvider())
        };
    }

    private static async Task<int> AddAppAsync(IServiceProvider provider, string name)
    {
        var db = provider.GetRequiredService<SsoDbContext>();
        var app = new TenantApp { Name = name, ReturnUrl = $"https://{name.Replace(' ', '-').ToLower()}.example.com/callback" };
        db.TenantApps.Add(app);
        await db.SaveChangesAsync();
        return app.Id;
    }

    private static async Task<int> AddGroupAsync(IServiceProvider provider, int appId, string fullName, int level)
    {
        var db = provider.GetRequiredService<SsoDbContext>();
        var group = new Group { TenantAppId = appId, Name = fullName, PowerLevel = level };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    /// <summary>Reads from a fresh context so assertions see what was really persisted.</summary>
    private static async Task<List<Group>> ReadGroupsAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
        return await db.Groups.AsNoTracking().OrderBy(g => g.Id).ToListAsync();
    }

    private static GroupFormViewModel Form(int? appId, string name, int? level) =>
        new() { TenantAppId = appId, GroupName = name, PowerLevel = level };

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    // ------------------------------------------- name auto-prefixed correctly

    [Fact]
    public async Task Create_PrefixesGroupNameWithAppName_AndSavesLevel()
    {
        await using var provider = BuildServiceProvider(nameof(Create_PrefixesGroupNameWithAppName_AndSavesLevel));
        var appId = await AddAppAsync(provider, "HR Portal");
        var controller = BuildController(provider);

        var result = await controller.Create(Form(appId, "Admins", 0));

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(GroupsController.Index), redirect.ActionName);

        var saved = Assert.Single(await ReadGroupsAsync(provider));
        Assert.Equal("HR Portal-Admins", saved.Name);   // [AppName]-[GroupName]
        Assert.Equal(0, saved.PowerLevel);
        Assert.Equal(appId, saved.TenantAppId);
    }

    [Fact]
    public async Task Create_UsesTheNameOfTheSelectedApp()
    {
        await using var provider = BuildServiceProvider(nameof(Create_UsesTheNameOfTheSelectedApp));
        await AddAppAsync(provider, "HR Portal");
        var payrollId = await AddAppAsync(provider, "Payroll");
        var controller = BuildController(provider);

        await controller.Create(Form(payrollId, "Auditors", 5));

        var saved = Assert.Single(await ReadGroupsAsync(provider));
        Assert.Equal("Payroll-Auditors", saved.Name);
    }

    [Theory]
    [InlineData("Admins", "HR Portal-Admins")]
    [InlineData("  Admins  ", "HR Portal-Admins")]             // trimmed
    [InlineData("HR Portal-Admins", "HR Portal-Admins")]       // prefix already typed: not doubled
    [InlineData("hr portal-Admins", "HR Portal-Admins")]       // ...in any casing
    [InlineData("Super Admins", "HR Portal-Super Admins")]     // spaces inside the name are kept
    public async Task Create_NormalizesTheTypedName_BeforePrefixing(string typed, string expectedStoredName)
    {
        await using var provider = BuildServiceProvider(nameof(Create_NormalizesTheTypedName_BeforePrefixing));
        var appId = await AddAppAsync(provider, "HR Portal");
        var controller = BuildController(provider);

        var result = await controller.Create(Form(appId, typed, 1));

        Assert.IsType<RedirectToActionResult>(result);
        var saved = Assert.Single(await ReadGroupsAsync(provider));
        Assert.Equal(expectedStoredName, saved.Name);
    }

    // ------------------------------------------------- level validation works

    [Theory]
    [InlineData(0)]     // highest power
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(GroupRules.MaxPowerLevel)]
    public async Task Create_AcceptsLevelsInsideTheAllowedRange(int level)
    {
        await using var provider = BuildServiceProvider(nameof(Create_AcceptsLevelsInsideTheAllowedRange));
        var appId = await AddAppAsync(provider, "HR Portal");
        var controller = BuildController(provider);

        var result = await controller.Create(Form(appId, "Team", level));

        Assert.IsType<RedirectToActionResult>(result);
        var saved = Assert.Single(await ReadGroupsAsync(provider));
        Assert.Equal(level, saved.PowerLevel);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(GroupRules.MaxPowerLevel + 1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task Create_RejectsLevelsOutsideTheAllowedRange(int level)
    {
        await using var provider = BuildServiceProvider(nameof(Create_RejectsLevelsOutsideTheAllowedRange));
        var appId = await AddAppAsync(provider, "HR Portal");
        var controller = BuildController(provider);

        var result = await controller.Create(Form(appId, "Team", level));

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<GroupFormViewModel>(view.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(GroupFormViewModel.PowerLevel)));
        Assert.Empty(await ReadGroupsAsync(provider));
    }

    [Fact]
    public async Task Create_RejectsMissingLevel_InsteadOfDefaultingToZero()
    {
        // 0 is the HIGHEST power, so an empty field must never silently become 0.
        await using var provider = BuildServiceProvider(nameof(Create_RejectsMissingLevel_InsteadOfDefaultingToZero));
        var appId = await AddAppAsync(provider, "HR Portal");
        var controller = BuildController(provider);

        var result = await controller.Create(Form(appId, "Team", level: null));

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(GroupFormViewModel.PowerLevel)));
        Assert.Empty(await ReadGroupsAsync(provider));
    }

    [Fact]
    public async Task Edit_RejectsInvalidLevel_AndLeavesTheGroupUnchanged()
    {
        await using var provider = BuildServiceProvider(nameof(Edit_RejectsInvalidLevel_AndLeavesTheGroupUnchanged));
        var appId = await AddAppAsync(provider, "HR Portal");
        var groupId = await AddGroupAsync(provider, appId, "HR Portal-Staff", 10);
        var controller = BuildController(provider);

        var result = await controller.Edit(groupId, Form(appId, "Staff", -3));

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(GroupFormViewModel.PowerLevel)));
        var saved = Assert.Single(await ReadGroupsAsync(provider));
        Assert.Equal(10, saved.PowerLevel);
    }

    // -------------------------------------------------------- unique per app

    [Theory]
    [InlineData("Admins")]
    [InlineData("admins")]       // case-insensitive
    [InlineData("  ADMINS ")]
    public async Task Create_RejectsDuplicateNameWithinTheSameApp(string duplicate)
    {
        await using var provider = BuildServiceProvider(nameof(Create_RejectsDuplicateNameWithinTheSameApp));
        var appId = await AddAppAsync(provider, "HR Portal");
        await AddGroupAsync(provider, appId, "HR Portal-Admins", 0);
        var controller = BuildController(provider);

        var result = await controller.Create(Form(appId, duplicate, 3));

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(GroupFormViewModel.GroupName)));
        Assert.Single(await ReadGroupsAsync(provider));
    }

    [Fact]
    public async Task Create_AllowsTheSameNameInDifferentApps()
    {
        await using var provider = BuildServiceProvider(nameof(Create_AllowsTheSameNameInDifferentApps));
        var hrId = await AddAppAsync(provider, "HR Portal");
        var payrollId = await AddAppAsync(provider, "Payroll");
        var controller = BuildController(provider);

        Assert.IsType<RedirectToActionResult>(await controller.Create(Form(hrId, "Admins", 0)));
        Assert.IsType<RedirectToActionResult>(await controller.Create(Form(payrollId, "Admins", 0)));

        var names = (await ReadGroupsAsync(provider)).Select(g => g.Name).ToList();
        Assert.Equal(new[] { "HR Portal-Admins", "Payroll-Admins" }, names);
    }

    [Fact]
    public async Task Edit_RejectsRenamingOntoAnotherGroupOfTheSameApp_ButAllowsKeepingItsOwnName()
    {
        await using var provider = BuildServiceProvider(nameof(Edit_RejectsRenamingOntoAnotherGroupOfTheSameApp_ButAllowsKeepingItsOwnName));
        var appId = await AddAppAsync(provider, "HR Portal");
        await AddGroupAsync(provider, appId, "HR Portal-Admins", 0);
        var staffId = await AddGroupAsync(provider, appId, "HR Portal-Staff", 10);
        var controller = BuildController(provider);

        var clash = await controller.Edit(staffId, Form(appId, "admins", 10));
        Assert.IsType<ViewResult>(clash);
        Assert.True(controller.ModelState.ContainsKey(nameof(GroupFormViewModel.GroupName)));

        var controller2 = BuildController(provider);
        var sameName = await controller2.Edit(staffId, Form(appId, "Staff", 15));
        Assert.IsType<RedirectToActionResult>(sameName);
    }

    // ------------------------------------------------------- other validation

    [Fact]
    public async Task Create_RejectsUnknownApp()
    {
        await using var provider = BuildServiceProvider(nameof(Create_RejectsUnknownApp));
        var controller = BuildController(provider);

        var result = await controller.Create(Form(appId: 9999, "Team", 1));

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(GroupFormViewModel.TenantAppId)));
        Assert.Empty(await ReadGroupsAsync(provider));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("HR Portal-")]   // just the prefix, no actual name
    public async Task Create_RejectsBlankNames(string typed)
    {
        await using var provider = BuildServiceProvider(nameof(Create_RejectsBlankNames));
        var appId = await AddAppAsync(provider, "HR Portal");
        var controller = BuildController(provider);

        var result = await controller.Create(Form(appId, typed, 1));

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(GroupFormViewModel.GroupName)));
        Assert.Empty(await ReadGroupsAsync(provider));
    }

    [Fact]
    public async Task Create_RejectsNamesThatAreTooLongOnceTheAppPrefixIsAdded()
    {
        await using var provider = BuildServiceProvider(nameof(Create_RejectsNamesThatAreTooLongOnceTheAppPrefixIsAdded));
        var appId = await AddAppAsync(provider, "HR Portal");
        var controller = BuildController(provider);

        // 95 characters is fine on its own but "HR Portal-" pushes it past 100.
        var result = await controller.Create(Form(appId, new string('x', 95), 1));

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(GroupFormViewModel.GroupName)));
        Assert.Empty(await ReadGroupsAsync(provider));
    }

    // ------------------------------------------------------------------- list

    [Fact]
    public async Task Index_ListsAllGroups_OrderedByAppThenPowerLevel()
    {
        await using var provider = BuildServiceProvider(nameof(Index_ListsAllGroups_OrderedByAppThenPowerLevel));
        var hrId = await AddAppAsync(provider, "HR Portal");
        var payrollId = await AddAppAsync(provider, "Payroll");
        await AddGroupAsync(provider, hrId, "HR Portal-Staff", 10);
        await AddGroupAsync(provider, payrollId, "Payroll-Auditors", 5);
        await AddGroupAsync(provider, hrId, "HR Portal-Admins", 0);
        var controller = BuildController(provider);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyList<GroupListItemViewModel>>(view.Model);
        Assert.Equal(
            new[] { "HR Portal-Admins", "HR Portal-Staff", "Payroll-Auditors" },
            model.Select(g => g.Name).ToArray());
        Assert.Equal("HR Portal", model[0].AppName);
        Assert.Equal(0, model[0].PowerLevel);
    }

    [Fact]
    public async Task CreateGet_OffersEveryRegisteredApp()
    {
        await using var provider = BuildServiceProvider(nameof(CreateGet_OffersEveryRegisteredApp));
        await AddAppAsync(provider, "Payroll");
        await AddAppAsync(provider, "HR Portal");
        var controller = BuildController(provider);

        var result = await controller.Create();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<GroupFormViewModel>(view.Model);
        Assert.Equal(new[] { "HR Portal", "Payroll" }, model.Apps.Select(a => a.Name).ToArray());
        Assert.Null(model.PowerLevel);   // no default level
    }

    // ------------------------------------------------------------------- edit

    [Fact]
    public async Task EditGet_ShowsTheNameWithoutThePrefix()
    {
        await using var provider = BuildServiceProvider(nameof(EditGet_ShowsTheNameWithoutThePrefix));
        var appId = await AddAppAsync(provider, "HR Portal");
        var groupId = await AddGroupAsync(provider, appId, "HR Portal-Staff", 10);
        var controller = BuildController(provider);

        var result = await controller.Edit(groupId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<GroupFormViewModel>(view.Model);
        Assert.Equal("Staff", model.GroupName);
        Assert.Equal("HR Portal", model.AppName);
        Assert.Equal(10, model.PowerLevel);
    }

    [Fact]
    public async Task EditPost_UpdatesNameAndLevel_AndKeepsThePrefix()
    {
        await using var provider = BuildServiceProvider(nameof(EditPost_UpdatesNameAndLevel_AndKeepsThePrefix));
        var appId = await AddAppAsync(provider, "HR Portal");
        var groupId = await AddGroupAsync(provider, appId, "HR Portal-Staff", 10);
        var controller = BuildController(provider);

        var result = await controller.Edit(groupId, Form(appId, "Employees", 20));

        Assert.IsType<RedirectToActionResult>(result);
        var saved = Assert.Single(await ReadGroupsAsync(provider));
        Assert.Equal("HR Portal-Employees", saved.Name);
        Assert.Equal(20, saved.PowerLevel);
    }

    [Fact]
    public async Task EditPost_IgnoresAPostedAppId_SoTheGroupStaysWithItsApp()
    {
        await using var provider = BuildServiceProvider(nameof(EditPost_IgnoresAPostedAppId_SoTheGroupStaysWithItsApp));
        var hrId = await AddAppAsync(provider, "HR Portal");
        var payrollId = await AddAppAsync(provider, "Payroll");
        var groupId = await AddGroupAsync(provider, hrId, "HR Portal-Staff", 10);
        var controller = BuildController(provider);

        await controller.Edit(groupId, Form(payrollId, "Staff", 10));

        var saved = Assert.Single(await ReadGroupsAsync(provider));
        Assert.Equal(hrId, saved.TenantAppId);
        Assert.Equal("HR Portal-Staff", saved.Name);
    }

    [Fact]
    public async Task Edit_WithUnknownId_ReturnsNotFound()
    {
        await using var provider = BuildServiceProvider(nameof(Edit_WithUnknownId_ReturnsNotFound));
        var controller = BuildController(provider);

        Assert.IsType<NotFoundResult>(await controller.Edit(12345));
        Assert.IsType<NotFoundResult>(await controller.Edit(12345, Form(1, "Team", 1)));
    }

    // ----------------------------------------------------------------- delete

    [Fact]
    public async Task Delete_RemovesTheGroup_AndOnlyThatGroup()
    {
        await using var provider = BuildServiceProvider(nameof(Delete_RemovesTheGroup_AndOnlyThatGroup));
        var appId = await AddAppAsync(provider, "HR Portal");
        var adminsId = await AddGroupAsync(provider, appId, "HR Portal-Admins", 0);
        await AddGroupAsync(provider, appId, "HR Portal-Staff", 10);
        var controller = BuildController(provider);

        var result = await controller.Delete(adminsId);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(GroupsController.Index), redirect.ActionName);
        var remaining = Assert.Single(await ReadGroupsAsync(provider));
        Assert.Equal("HR Portal-Staff", remaining.Name);
    }

    [Fact]
    public async Task Delete_AlsoRemovesTheGroupsMemberships()
    {
        await using var provider = BuildServiceProvider(nameof(Delete_AlsoRemovesTheGroupsMemberships));
        var appId = await AddAppAsync(provider, "HR Portal");
        var groupId = await AddGroupAsync(provider, appId, "HR Portal-Admins", 0);

        var db = provider.GetRequiredService<SsoDbContext>();
        var user = new ApplicationUser { UserName = "member@example.com", Email = "member@example.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = groupId });
        await db.SaveChangesAsync();

        await BuildController(provider).Delete(groupId);

        using var scope = provider.CreateScope();
        var fresh = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
        Assert.Empty(await fresh.UserGroups.ToListAsync());
        Assert.Empty(await fresh.Groups.ToListAsync());
        Assert.Single(await fresh.Users.ToListAsync());   // the user itself is untouched
    }

    [Fact]
    public async Task Delete_WithUnknownId_ReturnsNotFound()
    {
        await using var provider = BuildServiceProvider(nameof(Delete_WithUnknownId_ReturnsNotFound));
        var controller = BuildController(provider);

        Assert.IsType<NotFoundResult>(await controller.Delete(12345));
    }

    // ------------------------------------------------------------------ audit

    [Fact]
    public async Task CreateEditDelete_AreWrittenToTheAuditLog()
    {
        await using var provider = BuildServiceProvider(nameof(CreateEditDelete_AreWrittenToTheAuditLog));
        var appId = await AddAppAsync(provider, "HR Portal");
        var controller = BuildController(provider);

        await controller.Create(Form(appId, "Staff", 10));
        var groupId = (await ReadGroupsAsync(provider)).Single().Id;
        await controller.Edit(groupId, Form(appId, "Staff", 11));
        await controller.Delete(groupId);

        using var scope = provider.CreateScope();
        var actions = (await scope.ServiceProvider.GetRequiredService<SsoDbContext>()
            .AuditLogs.AsNoTracking().OrderBy(a => a.Id).Select(a => a.Action).ToListAsync());
        Assert.Equal(new[] { "CreateGroup", "UpdateGroup", "DeleteGroup" }, actions);
    }

    // ------------------------------------- renaming an app updates its groups

    [Fact]
    public async Task RenamingAnApp_UpdatesThePrefixOfItsGroups()
    {
        await using var provider = BuildServiceProvider(nameof(RenamingAnApp_UpdatesThePrefixOfItsGroups));
        var hrId = await AddAppAsync(provider, "HR Portal");
        var payrollId = await AddAppAsync(provider, "Payroll");
        await AddGroupAsync(provider, hrId, "HR Portal-Admins", 0);
        await AddGroupAsync(provider, hrId, "HR Portal-Staff", 10);
        await AddGroupAsync(provider, payrollId, "Payroll-Auditors", 5);

        var apps = new TenantAppsController(provider.GetRequiredService<SsoDbContext>());
        var result = await apps.Edit(hrId, new ExternalApp { Name = "People Portal", ReturnUrl = "https://hr-portal.example.com/callback" });

        Assert.IsType<RedirectToActionResult>(result);
        var names = (await ReadGroupsAsync(provider)).Select(g => g.Name).ToList();
        Assert.Equal(new[] { "People Portal-Admins", "People Portal-Staff", "Payroll-Auditors" }, names);
    }
}
