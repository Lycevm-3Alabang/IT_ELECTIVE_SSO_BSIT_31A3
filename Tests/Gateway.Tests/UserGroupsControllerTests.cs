using Data;
using Gateway.Areas.Admin.Controllers;
using Gateway.Areas.Admin.Models.Users;
using Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;
using AdminUsersController = Gateway.Areas.Admin.Controllers.UsersController;

namespace Gateway.Tests;

public class UserGroupsControllerTests
{
    // ---------------------------------------------------------------- helpers

    private static ServiceProvider BuildServiceProvider(string testName)
    {
        // Unique store per call so tests never share data.
        var dbName = $"{testName}-{Guid.NewGuid()}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SsoDbContext>(options =>
            options.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<SsoDbContext>();
        services.AddScoped<IAuditService, AuditService>();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<SsoDbContext>().Database.EnsureCreated();
        return provider;
    }

    private static UserGroupsController BuildController(IServiceProvider provider, bool ajax = false)
    {
        var httpContext = new DefaultHttpContext();
        if (ajax)
        {
            httpContext.Request.Headers.XRequestedWith = "XMLHttpRequest";
        }

        return new UserGroupsController(
            provider.GetRequiredService<SsoDbContext>(),
            provider.GetRequiredService<IAuditService>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NullTempDataProvider())
        };
    }

    private static async Task<string> AddUserAsync(IServiceProvider provider, string email)
    {
        var db = provider.GetRequiredService<SsoDbContext>();
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
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
    private static async Task<List<UserGroup>> ReadLinksAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
        return await db.UserGroups.AsNoTracking().OrderBy(l => l.UserId).ThenBy(l => l.GroupId).ToListAsync();
    }

    private static async Task<List<int>> GroupIdsOfAsync(IServiceProvider provider, string userId) =>
        (await ReadLinksAsync(provider)).Where(l => l.UserId == userId).Select(l => l.GroupId).OrderBy(i => i).ToList();

    private static string? Temp(Controller controller, string key) => controller.TempData[key] as string;

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    /// <summary>Seeds two apps with two groups each; returns their ids.</summary>
    private sealed record Seed(int HrAdmins, int HrStaff, int PayrollAuditors, int PayrollClerks);

    private static async Task<Seed> SeedGroupsAsync(IServiceProvider provider)
    {
        var hr = await AddAppAsync(provider, "HR Portal");
        var payroll = await AddAppAsync(provider, "Payroll");
        return new Seed(
            await AddGroupAsync(provider, hr, "HR Portal-Admins", 0),
            await AddGroupAsync(provider, hr, "HR Portal-Staff", 10),
            await AddGroupAsync(provider, payroll, "Payroll-Auditors", 5),
            await AddGroupAsync(provider, payroll, "Payroll-Clerks", 20));
    }

    // ------------------------------- user can be assigned to multiple groups

    [Fact]
    public async Task Assign_UserCanBeAssignedToMultipleGroups_AcrossDifferentApps()
    {
        await using var provider = BuildServiceProvider(nameof(Assign_UserCanBeAssignedToMultipleGroups_AcrossDifferentApps));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var controller = BuildController(provider);

        // one group in HR Portal, one in Payroll, and a second one in HR Portal
        Assert.IsType<RedirectToActionResult>(await controller.Assign(userId, g.HrAdmins));
        Assert.IsType<RedirectToActionResult>(await controller.Assign(userId, g.PayrollAuditors));
        Assert.IsType<RedirectToActionResult>(await controller.Assign(userId, g.HrStaff));

        var assigned = await GroupIdsOfAsync(provider, userId);
        Assert.Equal(new[] { g.HrAdmins, g.HrStaff, g.PayrollAuditors }.OrderBy(i => i).ToList(), assigned);
        Assert.Equal(3, assigned.Count);
    }

    [Fact]
    public async Task Assign_SuccessRedirectsToTheUsersDetailsPage_WithAConfirmation()
    {
        await using var provider = BuildServiceProvider(nameof(Assign_SuccessRedirectsToTheUsersDetailsPage_WithAConfirmation));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var controller = BuildController(provider);

        var result = await controller.Assign(userId, g.HrAdmins);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Users", redirect.ControllerName);
        Assert.Equal(userId, redirect.RouteValues!["id"]);
        Assert.Contains("HR Portal-Admins", Temp(controller, "StatusMessage"));
    }

    [Fact]
    public async Task Assign_DifferentUsersCanShareTheSameGroup()
    {
        await using var provider = BuildServiceProvider(nameof(Assign_DifferentUsersCanShareTheSameGroup));
        var g = await SeedGroupsAsync(provider);
        var alice = await AddUserAsync(provider, "alice@example.com");
        var bob = await AddUserAsync(provider, "bob@example.com");
        var controller = BuildController(provider);

        await controller.Assign(alice, g.HrAdmins);
        await controller.Assign(bob, g.HrAdmins);

        Assert.Equal(new[] { g.HrAdmins }, await GroupIdsOfAsync(provider, alice));
        Assert.Equal(new[] { g.HrAdmins }, await GroupIdsOfAsync(provider, bob));
    }

    [Fact]
    public async Task Assign_Ajax_ReturnsJsonDescribingTheNewMembership()
    {
        await using var provider = BuildServiceProvider(nameof(Assign_Ajax_ReturnsJsonDescribingTheNewMembership));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var controller = BuildController(provider, ajax: true);

        var result = await controller.Assign(userId, g.PayrollAuditors);

        var json = Assert.IsType<JsonResult>(result);
        var group = (UserGroupItemViewModel)json.Value!.GetType().GetProperty("group")!.GetValue(json.Value)!;
        Assert.Equal(g.PayrollAuditors, group.GroupId);
        Assert.Equal("Payroll-Auditors", group.GroupName);
        Assert.Equal("Payroll", group.AppName);
        Assert.Equal(5, group.PowerLevel);
    }

    // --------------------------------------------- unassign removes relationship

    [Fact]
    public async Task Unassign_RemovesTheRelationship_AndNothingElse()
    {
        await using var provider = BuildServiceProvider(nameof(Unassign_RemovesTheRelationship_AndNothingElse));
        var g = await SeedGroupsAsync(provider);
        var alice = await AddUserAsync(provider, "alice@example.com");
        var bob = await AddUserAsync(provider, "bob@example.com");
        var controller = BuildController(provider);
        await controller.Assign(alice, g.HrAdmins);
        await controller.Assign(alice, g.PayrollAuditors);
        await controller.Assign(bob, g.HrAdmins);

        var result = await controller.Unassign(alice, g.HrAdmins);

        Assert.IsType<RedirectToActionResult>(result);
        // the relationship is gone ...
        Assert.DoesNotContain(await ReadLinksAsync(provider), l => l.UserId == alice && l.GroupId == g.HrAdmins);
        // ... but her other group and bob's membership of the same group are untouched
        Assert.Equal(new[] { g.PayrollAuditors }, await GroupIdsOfAsync(provider, alice));
        Assert.Equal(new[] { g.HrAdmins }, await GroupIdsOfAsync(provider, bob));

        // ... and neither the user nor the group was deleted
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(4, await db.Groups.CountAsync());
    }

    [Fact]
    public async Task Unassign_Ajax_ReturnsJsonSuccess()
    {
        await using var provider = BuildServiceProvider(nameof(Unassign_Ajax_ReturnsJsonSuccess));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        await BuildController(provider).Assign(userId, g.HrAdmins);

        var result = await BuildController(provider, ajax: true).Unassign(userId, g.HrAdmins);

        var json = Assert.IsType<JsonResult>(result);
        Assert.True((bool)json.Value!.GetType().GetProperty("success")!.GetValue(json.Value)!);
        Assert.Empty(await ReadLinksAsync(provider));
    }

    [Fact]
    public async Task Unassign_ThenAssignAgain_Works()
    {
        await using var provider = BuildServiceProvider(nameof(Unassign_ThenAssignAgain_Works));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var controller = BuildController(provider);

        await controller.Assign(userId, g.HrAdmins);
        await controller.Unassign(userId, g.HrAdmins);
        var result = await controller.Assign(userId, g.HrAdmins);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(new[] { g.HrAdmins }, await GroupIdsOfAsync(provider, userId));
    }

    [Fact]
    public async Task Unassign_WhenNotAMember_ReturnsNotFound_AndChangesNothing()
    {
        await using var provider = BuildServiceProvider(nameof(Unassign_WhenNotAMember_ReturnsNotFound_AndChangesNothing));
        var g = await SeedGroupsAsync(provider);
        var alice = await AddUserAsync(provider, "alice@example.com");
        var bob = await AddUserAsync(provider, "bob@example.com");
        var controller = BuildController(provider);
        await controller.Assign(bob, g.HrAdmins);

        Assert.IsType<NotFoundResult>(await controller.Unassign(alice, g.HrAdmins));     // alice isn't a member
        Assert.IsType<NotFoundResult>(await controller.Unassign("no-such-user", g.HrAdmins));
        Assert.IsType<NotFoundResult>(await controller.Unassign(bob, 9999));              // no such group

        Assert.Equal(new[] { g.HrAdmins }, await GroupIdsOfAsync(provider, bob));
    }

    // ------------------------------------------- cannot duplicate assignment

    [Fact]
    public async Task Assign_CannotDuplicateAnAssignment()
    {
        await using var provider = BuildServiceProvider(nameof(Assign_CannotDuplicateAnAssignment));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var controller = BuildController(provider);

        await controller.Assign(userId, g.HrAdmins);
        var second = BuildController(provider);
        var result = await second.Assign(userId, g.HrAdmins);

        // sent back to the details page with an error, and still only ONE row
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Contains("already in group", Temp(second, "ErrorMessage"));
        Assert.Null(Temp(second, "StatusMessage"));
        Assert.Single(await ReadLinksAsync(provider));
    }

    [Fact]
    public async Task Assign_Duplicate_Ajax_Returns409Conflict()
    {
        await using var provider = BuildServiceProvider(nameof(Assign_Duplicate_Ajax_Returns409Conflict));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        await BuildController(provider).Assign(userId, g.HrAdmins);

        var result = await BuildController(provider, ajax: true).Assign(userId, g.HrAdmins);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Single(await ReadLinksAsync(provider));
    }

    [Fact]
    public async Task Assign_Duplicate_DoesNotWriteASecondAuditEntry()
    {
        await using var provider = BuildServiceProvider(nameof(Assign_Duplicate_DoesNotWriteASecondAuditEntry));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var controller = BuildController(provider);

        await controller.Assign(userId, g.HrAdmins);
        await controller.Assign(userId, g.HrAdmins);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "AssignGroup"));
    }

    // -------------------------------------------------------- bad input

    [Fact]
    public async Task Assign_UnknownUser_ReturnsNotFound()
    {
        await using var provider = BuildServiceProvider(nameof(Assign_UnknownUser_ReturnsNotFound));
        var g = await SeedGroupsAsync(provider);

        Assert.IsType<NotFoundResult>(await BuildController(provider).Assign("no-such-user", g.HrAdmins));
        Assert.Empty(await ReadLinksAsync(provider));
    }

    [Fact]
    public async Task Assign_UnknownGroup_IsRejected_AndNothingIsSaved()
    {
        await using var provider = BuildServiceProvider(nameof(Assign_UnknownGroup_IsRejected_AndNothingIsSaved));
        await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");

        var form = BuildController(provider);
        Assert.IsType<RedirectToActionResult>(await form.Assign(userId, 9999));
        Assert.NotNull(Temp(form, "ErrorMessage"));

        var ajax = Assert.IsType<ObjectResult>(await BuildController(provider, ajax: true).Assign(userId, 9999));
        Assert.Equal(StatusCodes.Status404NotFound, ajax.StatusCode);

        Assert.Empty(await ReadLinksAsync(provider));
    }

    [Theory]
    [InlineData(0)]    // what model binding gives when the form field is missing
    [InlineData(-5)]
    public async Task Assign_MissingOrInvalidGroupId_IsABadRequest(int groupId)
    {
        await using var provider = BuildServiceProvider(nameof(Assign_MissingOrInvalidGroupId_IsABadRequest));
        await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");

        var result = await BuildController(provider, ajax: true).Assign(userId, groupId);

        var bad = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, bad.StatusCode);
        Assert.Empty(await ReadLinksAsync(provider));
    }

    // ----------------------------------------------------------- list (GET)

    [Fact]
    public async Task Index_ListsTheUsersGroups_AcrossApps_OrderedByAppThenPower()
    {
        await using var provider = BuildServiceProvider(nameof(Index_ListsTheUsersGroups_AcrossApps_OrderedByAppThenPower));
        var g = await SeedGroupsAsync(provider);
        var alice = await AddUserAsync(provider, "alice@example.com");
        var bob = await AddUserAsync(provider, "bob@example.com");
        var controller = BuildController(provider);
        await controller.Assign(alice, g.PayrollAuditors);
        await controller.Assign(alice, g.HrStaff);
        await controller.Assign(alice, g.HrAdmins);
        await controller.Assign(bob, g.PayrollClerks);       // must not appear in alice's list

        var result = await controller.Index(alice);

        var json = Assert.IsType<JsonResult>(result);
        var response = Assert.IsType<UserGroupListResponse>(json.Value);
        Assert.Equal(alice, response.UserId);
        Assert.Equal("alice@example.com", response.Email);
        Assert.Equal(
            new[] { "HR Portal-Admins", "HR Portal-Staff", "Payroll-Auditors" },
            response.Groups.Select(x => x.GroupName).ToArray());
        Assert.Equal(new[] { 0, 10, 5 }, response.Groups.Select(x => x.PowerLevel).ToArray());
        Assert.Equal(new[] { "HR Portal", "HR Portal", "Payroll" }, response.Groups.Select(x => x.AppName).ToArray());
    }

    [Fact]
    public async Task Index_ForAUserWithNoGroups_ReturnsAnEmptyList()
    {
        await using var provider = BuildServiceProvider(nameof(Index_ForAUserWithNoGroups_ReturnsAnEmptyList));
        var userId = await AddUserAsync(provider, "alice@example.com");

        var json = Assert.IsType<JsonResult>(await BuildController(provider).Index(userId));

        Assert.Empty(Assert.IsType<UserGroupListResponse>(json.Value).Groups);
    }

    [Fact]
    public async Task Index_UnknownUser_ReturnsNotFound()
    {
        await using var provider = BuildServiceProvider(nameof(Index_UnknownUser_ReturnsNotFound));

        Assert.IsType<NotFoundResult>(await BuildController(provider).Index("no-such-user"));
    }

    // ------------------------------------------------- user details page data

    [Fact]
    public async Task Details_ShowsAssignedGroups_AndOffersTheRest()
    {
        await using var provider = BuildServiceProvider(nameof(Details_ShowsAssignedGroups_AndOffersTheRest));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var assign = BuildController(provider);
        await assign.Assign(userId, g.HrAdmins);
        await assign.Assign(userId, g.PayrollAuditors);

        var users = new AdminUsersController(
            provider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>(),
            provider.GetRequiredService<SsoDbContext>(),
            provider.GetRequiredService<IAuditService>());
        var result = await users.Details(userId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<UserDetailsViewModel>(view.Model);
        Assert.Equal(new[] { "HR Portal-Admins", "Payroll-Auditors" }, model.Groups.Select(x => x.GroupName).ToArray());
        Assert.Equal(new[] { "HR Portal", "Payroll" }, model.Groups.Select(x => x.AppName).ToArray());
        // the dropdown only offers groups the user is NOT in
        Assert.Equal(
            new[] { "HR Portal-Staff", "Payroll-Clerks" },
            model.AvailableGroups.Select(x => x.GroupName).ToArray());
    }

    [Fact]
    public async Task Details_AfterUnassigning_TheGroupMovesBackToAvailable()
    {
        await using var provider = BuildServiceProvider(nameof(Details_AfterUnassigning_TheGroupMovesBackToAvailable));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var controller = BuildController(provider);
        await controller.Assign(userId, g.HrAdmins);
        await controller.Unassign(userId, g.HrAdmins);

        var users = new AdminUsersController(
            provider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>(),
            provider.GetRequiredService<SsoDbContext>(),
            provider.GetRequiredService<IAuditService>());
        var model = Assert.IsType<UserDetailsViewModel>(Assert.IsType<ViewResult>(await users.Details(userId)).Model);

        Assert.Empty(model.Groups);
        Assert.Contains(model.AvailableGroups, x => x.GroupId == g.HrAdmins);
    }

    // ---------------------------------------------------------------- audit

    [Fact]
    public async Task AssignAndUnassign_AreWrittenToTheAuditLog()
    {
        await using var provider = BuildServiceProvider(nameof(AssignAndUnassign_AreWrittenToTheAuditLog));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var controller = BuildController(provider);

        await controller.Assign(userId, g.HrAdmins);
        await controller.Unassign(userId, g.HrAdmins);

        using var scope = provider.CreateScope();
        var logs = await scope.ServiceProvider.GetRequiredService<SsoDbContext>()
            .AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(new[] { "AssignGroup", "UnassignGroup" }, logs.Select(a => a.Action).ToArray());
        Assert.Contains("alice@example.com", logs[0].Details);
        Assert.Contains("HR Portal-Admins", logs[0].Details);
    }

    // ------------------------------------ deleting a group cleans up members

    [Fact]
    public async Task DeletingAGroup_RemovesItFromUsersGroupLists()
    {
        await using var provider = BuildServiceProvider(nameof(DeletingAGroup_RemovesItFromUsersGroupLists));
        var g = await SeedGroupsAsync(provider);
        var userId = await AddUserAsync(provider, "alice@example.com");
        var controller = BuildController(provider);
        await controller.Assign(userId, g.HrAdmins);
        await controller.Assign(userId, g.PayrollAuditors);

        await new GroupsController(
            provider.GetRequiredService<SsoDbContext>(),
            provider.GetRequiredService<IAuditService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            TempData = controller.TempData
        }.Delete(g.HrAdmins);

        var json = Assert.IsType<JsonResult>(await controller.Index(userId));
        var remaining = Assert.IsType<UserGroupListResponse>(json.Value).Groups;
        Assert.Equal(new[] { "Payroll-Auditors" }, remaining.Select(x => x.GroupName).ToArray());
    }
}
