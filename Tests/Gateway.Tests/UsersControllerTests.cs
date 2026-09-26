using Data;
using Gateway.Controllers;
using Gateway.Models.Admin;
using Gateway.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http; 
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;

namespace Gateway.Tests;

public class UsersControllerTests
{
    private static ServiceProvider BuildServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();

        services.AddDbContext<SsoDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<SsoDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IReturnUrlValidator, ReturnUrlValidator>();

        return services.BuildServiceProvider();
    }

    private static UsersController BuildController(IServiceProvider provider)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var auditService = provider.GetRequiredService<IAuditService>();
        return new UsersController(userManager, auditService);
    }

    [Fact]
    public async Task Create_WithValidModel_CreatesUserWithHashedPassword()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithValidModel_CreatesUserWithHashedPassword));
        var controller = BuildController(provider);

        var model = new CreateUserViewModel
        {
            Email = "new.user@example.com",
            Password = "Sup3rSecret!",
            ConfirmPassword = "Sup3rSecret!",
        };

        var result = await controller.Create(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(UsersController.Index), redirect.ActionName);

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await userManager.FindByEmailAsync(model.Email);

        Assert.NotNull(created);
        Assert.True(created!.IsActive);
        Assert.NotNull(created.PasswordHash);
        Assert.NotEqual(model.Password, created.PasswordHash);
        Assert.True(await userManager.CheckPasswordAsync(created, model.Password));
    }

    [Fact]
    public async Task Create_WithInvalidModelState_DoesNotCreateUser()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithInvalidModelState_DoesNotCreateUser));
        var controller = BuildController(provider);
        controller.ModelState.AddModelError(nameof(CreateUserViewModel.Email), "Email is required.");

        var model = new CreateUserViewModel { Email = string.Empty, Password = "whatever", ConfirmPassword = "whatever" };

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Empty(userManager.Users.ToList());
    }

    [Fact]
    public async Task Create_WithDuplicateEmail_ReturnsViewWithModelError_AndDoesNotCreateSecondUser()
    {
        await using var provider = BuildServiceProvider(nameof(Create_WithDuplicateEmail_ReturnsViewWithModelError_AndDoesNotCreateSecondUser));
        var controller = BuildController(provider);

        const string email = "duplicate@example.com";

        var firstResult = await controller.Create(new CreateUserViewModel
        {
            Email = email,
            Password = "FirstPassword1!",
            ConfirmPassword = "FirstPassword1!",
        });
        Assert.IsType<RedirectToActionResult>(firstResult);

        var secondResult = await controller.Create(new CreateUserViewModel
        {
            Email = email,
            Password = "SecondPassword2!",
            ConfirmPassword = "SecondPassword2!",
        });

        var view = Assert.IsType<ViewResult>(secondResult);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(CreateUserViewModel.Email)));

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = provider.GetRequiredService<SsoDbContext>();
        var matchingUsers = dbContext.Users.Count(u => u.Email == email);

        Assert.Equal(1, matchingUsers);
        var existing = await userManager.FindByEmailAsync(email);
        Assert.True(await userManager.CheckPasswordAsync(existing!, "FirstPassword1!"));
    }

    [Fact]
    public async Task Index_ReturnsPagedUsers()
    {
        await using var provider = BuildServiceProvider(nameof(Index_ReturnsPagedUsers));
        var controller = BuildController(provider);

        for (var i = 0; i < 15; i++)
        {
            await controller.Create(new CreateUserViewModel
            {
                Email = $"user{i:00}@example.com",
                Password = "Password1!",
                ConfirmPassword = "Password1!",
            });
        }

        var result = await controller.Index(page: 2, pageSize: 10);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<UserListViewModel>(view.Model);

        Assert.Equal(15, model.TotalCount);
        Assert.Equal(2, model.TotalPages);
        Assert.Equal(5, model.Users.Count);
        Assert.False(model.HasNextPage);
        Assert.True(model.HasPreviousPage);
    }

    [Fact]
    public async Task Details_WithKnownId_ReturnsUser()
    {
        await using var provider = BuildServiceProvider(nameof(Details_WithKnownId_ReturnsUser));
        var controller = BuildController(provider);

        await controller.Create(new CreateUserViewModel
        {
            Email = "details@example.com",
            Password = "Password1!",
            ConfirmPassword = "Password1!",
        });

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("details@example.com");

        var result = await controller.Details(user!.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<UserDetailsViewModel>(view.Model);
        Assert.Equal("details@example.com", model.Email);
    }

    [Fact]
    public async Task Details_WithUnknownId_ReturnsNotFound()
    {
        await using var provider = BuildServiceProvider(nameof(Details_WithUnknownId_ReturnsNotFound));
        var controller = BuildController(provider);

        var result = await controller.Details("does-not-exist");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ToggleActive_ChangesIsActiveFromTrueToFalse()
    {
        await using var provider = BuildServiceProvider(nameof(ToggleActive_ChangesIsActiveFromTrueToFalse));
        var controller = BuildController(provider);

        await controller.Create(new CreateUserViewModel
        {
            Email = "toggle-off@example.com",
            Password = "Password1!",
            ConfirmPassword = "Password1!",
        });

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("toggle-off@example.com");
        Assert.NotNull(user);
        Assert.True(user!.IsActive);

        var result = await controller.ToggleActive(user.Id);

        Assert.IsType<RedirectToActionResult>(result);
        var reloaded = await userManager.FindByIdAsync(user.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task ToggleActive_ChangesIsActiveFromFalseToTrue()
    {
        await using var provider = BuildServiceProvider(nameof(ToggleActive_ChangesIsActiveFromFalseToTrue));
        var controller = BuildController(provider);

        await controller.Create(new CreateUserViewModel
        {
            Email = "toggle-on@example.com",
            Password = "Password1!",
            ConfirmPassword = "Password1!",
        });

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("toggle-on@example.com");
        Assert.NotNull(user);

        user!.IsActive = false;
        await userManager.UpdateAsync(user);

        var result = await controller.ToggleActive(user.Id);

        Assert.IsType<RedirectToActionResult>(result);
        var reloaded = await userManager.FindByIdAsync(user.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.IsActive);
    }

    [Fact]
    public async Task ToggleActive_Ajax_ReturnsJsonWithNewStatus()
    {
        await using var provider = BuildServiceProvider(nameof(ToggleActive_Ajax_ReturnsJsonWithNewStatus));
        var controller = BuildController(provider);

        await controller.Create(new CreateUserViewModel
        {
            Email = "toggle-ajax@example.com",
            Password = "Password1!",
            ConfirmPassword = "Password1!",
        });

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("toggle-ajax@example.com");
        Assert.NotNull(user);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.XRequestedWith = "XMLHttpRequest";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.ToggleActive(user!.Id);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var valueType = json.Value!.GetType();
        Assert.True((bool)valueType.GetProperty("success")!.GetValue(json.Value)!);
        Assert.False((bool)valueType.GetProperty("isActive")!.GetValue(json.Value)!);
        Assert.Equal("Inactive", valueType.GetProperty("status")!.GetValue(json.Value));
    }

    [Fact]
    public async Task InactiveUser_CannotCompletePasswordSignIn()
    {
        await using var provider = BuildServiceProvider(nameof(InactiveUser_CannotCompletePasswordSignIn));
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = "suspended@example.com",
            Email = "suspended@example.com",
            EmailConfirmed = true,
            IsActive = false,
        };

        var createResult = await userManager.CreateAsync(user, "Password1!");
        Assert.True(createResult.Succeeded);

        var result = await signInManager.PasswordSignInAsync(user.Email!, "Password1!", false, false);

        Assert.False(result.Succeeded);
        Assert.True(result.IsNotAllowed);
    }

    [Fact]
    public async Task Delete_SetsIsActiveFalse_InsteadOfRemovingRow()
    {
        await using var provider = BuildServiceProvider(nameof(Delete_SetsIsActiveFalse_InsteadOfRemovingRow));
        var controller = BuildController(provider);

        await controller.Create(new CreateUserViewModel
        {
            Email = "todelete@example.com",
            Password = "Password1!",
            ConfirmPassword = "Password1!",
        });

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("todelete@example.com");

        var result = await controller.Delete(user!.Id);

        Assert.IsType<RedirectToActionResult>(result);

        var reloaded = await userManager.FindByIdAsync(user.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
    }
    [Fact]
    public async Task Login_CreatesSuccessfulAuditLog()
    {
        await using var provider = BuildServiceProvider(nameof(Login_CreatesSuccessfulAuditLog));
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();
        var auditService = provider.GetRequiredService<IAuditService>();

        var user = new ApplicationUser
        {
            UserName = "login.audit@example.com",
            Email = "login.audit@example.com",
            EmailConfirmed = true,
            IsActive = true
        };
        Assert.True((await userManager.CreateAsync(user, "Password1!")).Succeeded);

        var returnUrlValidator = provider.GetRequiredService<IReturnUrlValidator>();
        var controller = new Gateway.Controllers.AccountController(signInManager, userManager, auditService, returnUrlValidator);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        var result = await controller.Login(user.Email!, "Password1!", false, null);

        Assert.IsType<LocalRedirectResult>(result);
        var log = await provider.GetRequiredService<SsoDbContext>().AuditLogs.SingleAsync(x => x.Action == "LoginSuccess");
        Assert.Equal(user.Id, log.UserId);
        Assert.Contains(user.Email!, log.Details);
        Assert.Equal("127.0.0.1", log.IpAddress);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_CreatesFailedAuditLogWithEmailAndReason()
    {
        await using var provider = BuildServiceProvider(nameof(Login_WithInvalidPassword_CreatesFailedAuditLogWithEmailAndReason));
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();
        var auditService = provider.GetRequiredService<IAuditService>();

        var user = new ApplicationUser
        {
            UserName = "wrong.password@example.com",
            Email = "wrong.password@example.com",
            EmailConfirmed = true,
            IsActive = true
        };
        Assert.True((await userManager.CreateAsync(user, "Password1!")).Succeeded);

        var returnUrlValidator = provider.GetRequiredService<IReturnUrlValidator>();
        var controller = new Gateway.Controllers.AccountController(signInManager, userManager, auditService, returnUrlValidator);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.5");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Login(user.Email!, "TotallyWrongPassword!", false, null);

        Assert.IsType<ViewResult>(result);
        var log = await provider.GetRequiredService<SsoDbContext>().AuditLogs.SingleAsync(x => x.Action == "LoginFailed");
        Assert.Equal(user.Email, log.Email);
        Assert.Equal("Invalid email or password", log.Reason);
        Assert.Equal("192.168.1.5", log.IpAddress);
    }

    [Fact]
    public async Task Login_WithInactiveAccount_CreatesFailedAuditLogWithReason()
    {
        await using var provider = BuildServiceProvider(nameof(Login_WithInactiveAccount_CreatesFailedAuditLogWithReason));
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();
        var auditService = provider.GetRequiredService<IAuditService>();

        var user = new ApplicationUser
        {
            UserName = "suspended.login@example.com",
            Email = "suspended.login@example.com",
            EmailConfirmed = true,
            IsActive = false
        };
        Assert.True((await userManager.CreateAsync(user, "Password1!")).Succeeded);

        var returnUrlValidator = provider.GetRequiredService<IReturnUrlValidator>();
        var controller = new Gateway.Controllers.AccountController(signInManager, userManager, auditService, returnUrlValidator);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.Login(user.Email!, "Password1!", false, null);

        Assert.IsType<ViewResult>(result);
        var log = await provider.GetRequiredService<SsoDbContext>().AuditLogs.SingleAsync(x => x.Action == "LoginFailed");
        Assert.Equal(user.Email, log.Email);
        Assert.Equal("Account inactive", log.Reason);
    }

    [Fact]
    public async Task ToggleActive_CreatesAdminActionAuditLog()
    {
        await using var provider = BuildServiceProvider(nameof(ToggleActive_CreatesAdminActionAuditLog));
        var controller = BuildController(provider);

        await controller.Create(new CreateUserViewModel
        {
            Email = "audit.target@example.com",
            Password = "Password1!",
            ConfirmPassword = "Password1!"
        });

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("audit.target@example.com");
        Assert.NotNull(user);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.10");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.ToggleActive(user!.Id);

        var log = await provider.GetRequiredService<SsoDbContext>().AuditLogs.SingleAsync(x => x.Action == "ToggleActive");
        Assert.Contains(user.Email!, log.Details);
        Assert.Equal("10.0.0.10", log.IpAddress);
    }

    [Fact]
    public async Task LoginGet_WithReturnUrlForRegisteredApp_ShowsLoginForm()
    {
        await using var provider = BuildServiceProvider(nameof(LoginGet_WithReturnUrlForRegisteredApp_ShowsLoginForm));
        var db = provider.GetRequiredService<SsoDbContext>();
        db.TenantApps.Add(new TenantApp { Name = "Approved App", ReturnUrl = "https://approved.example.com/callback", IsEnabled = true });
        await db.SaveChangesAsync();

        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var auditService = provider.GetRequiredService<IAuditService>();
        var returnUrlValidator = provider.GetRequiredService<IReturnUrlValidator>();
        var controller = new Gateway.Controllers.AccountController(signInManager, userManager, auditService, returnUrlValidator);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.Login("https://approved.example.com/callback");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Null(view.ViewName);
    }

    [Fact]
    public async Task LoginGet_WithReturnUrlForUnregisteredApp_ShowsUnapprovedAppView()
    {
        await using var provider = BuildServiceProvider(nameof(LoginGet_WithReturnUrlForUnregisteredApp_ShowsUnapprovedAppView));
        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var auditService = provider.GetRequiredService<IAuditService>();
        var returnUrlValidator = provider.GetRequiredService<IReturnUrlValidator>();
        var controller = new Gateway.Controllers.AccountController(signInManager, userManager, auditService, returnUrlValidator);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.Login("https://never-registered.example.com/callback");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("UnapprovedApp", view.ViewName);

        var db = provider.GetRequiredService<SsoDbContext>();
        var log = await db.AuditLogs.SingleAsync(x => x.Action == "InvalidReturnUrl");
        Assert.Contains("never-registered.example.com", log.Details);
    }

    [Fact]
    public async Task LoginPost_WithValidCredentialsAndRegisteredReturnUrl_RedirectsToTenantApp()
    {
        await using var provider = BuildServiceProvider(nameof(LoginPost_WithValidCredentialsAndRegisteredReturnUrl_RedirectsToTenantApp));
        var db = provider.GetRequiredService<SsoDbContext>();
        db.TenantApps.Add(new TenantApp { Name = "Approved App", ReturnUrl = "https://approved.example.com/callback", IsEnabled = true });
        await db.SaveChangesAsync();

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();
        var auditService = provider.GetRequiredService<IAuditService>();
        var returnUrlValidator = provider.GetRequiredService<IReturnUrlValidator>();

        var user = new ApplicationUser
        {
            UserName = "sso.user@example.com",
            Email = "sso.user@example.com",
            EmailConfirmed = true,
            IsActive = true
        };
        Assert.True((await userManager.CreateAsync(user, "Password1!")).Succeeded);

        var controller = new Gateway.Controllers.AccountController(signInManager, userManager, auditService, returnUrlValidator);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.Login(user.Email!, "Password1!", false, "https://approved.example.com/callback");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://approved.example.com/callback", redirect.Url);
    }

    [Fact]
    public async Task LoginPost_WithUnregisteredReturnUrl_DoesNotSignInAndShowsUnapprovedAppView()
    {
        await using var provider = BuildServiceProvider(nameof(LoginPost_WithUnregisteredReturnUrl_DoesNotSignInAndShowsUnapprovedAppView));
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();
        var auditService = provider.GetRequiredService<IAuditService>();
        var returnUrlValidator = provider.GetRequiredService<IReturnUrlValidator>();

        var user = new ApplicationUser
        {
            UserName = "blocked.user@example.com",
            Email = "blocked.user@example.com",
            EmailConfirmed = true,
            IsActive = true
        };
        Assert.True((await userManager.CreateAsync(user, "Password1!")).Succeeded);

        var controller = new Gateway.Controllers.AccountController(signInManager, userManager, auditService, returnUrlValidator);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.Login(user.Email!, "Password1!", false, "https://not-registered.example.com/callback");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("UnapprovedApp", view.ViewName);

        var db = provider.GetRequiredService<SsoDbContext>();
        Assert.DoesNotContain(db.AuditLogs, x => x.Action == "LoginSuccess");
        Assert.Contains(db.AuditLogs, x => x.Action == "InvalidReturnUrl");
    }

}
