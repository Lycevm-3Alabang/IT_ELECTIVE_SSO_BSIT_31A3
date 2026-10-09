using System.Net;
using System.Text;
using Data;
using Gateway.Controllers;
using Gateway.Models;
using Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Models;
using Xunit;

namespace Gateway.Tests;

public class AuthControllerTests
{
    private const string AppName = "Sample App";
    private const string ValidReturnUrl = "https://example.com/callback";
    private const string Password = "Passw0rd!x";
    private const string SecretKey = "unit-test-secret-key-that-is-long-enough-for-hs256";

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public TestTimeProvider Time { get; } = new();

        public Harness(string dbName)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddDbContext<SsoDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<SsoDbContext>()
                .AddDefaultTokenProviders();
            // Same registration as Program.cs, so the tests exercise the real active-aware manager.
            services.AddScoped<SignInManager<ApplicationUser>, ActiveUserSignInManager>();
            services.AddScoped<IAuditService, AuditService>();

            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IExternalAppRegistry, InMemoryExternalAppRegistry>();
            services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();
            services.AddSingleton<IJwtTokenService, JwtTokenService>();
            services.AddSingleton(Options.Create(new JwtSettings
            {
                Issuer = "SSOGateway",
                Audience = "SSOClientApps",
                SecretKey = SecretKey,
                ExpiryHours = 9
            }));
            services.AddSingleton(Options.Create(new LoginRateLimitSettings()));

            Provider = services.BuildServiceProvider();
        }

        public IExternalAppRegistry Registry => Provider.GetRequiredService<IExternalAppRegistry>();

        /// <summary>A fresh controller per call, like a real request. Singletons (limiter, registry) are shared.</summary>
        public AuthController NewController(string ip = "203.0.113.5")
        {
            var scope = Provider.CreateScope();
            var sp = scope.ServiceProvider;
            var controller = new AuthController(
                sp.GetRequiredService<SignInManager<ApplicationUser>>(),
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                sp.GetRequiredService<SsoDbContext>(),
                sp.GetRequiredService<IExternalAppRegistry>(),
                sp.GetRequiredService<IJwtTokenService>(),
                sp.GetRequiredService<ILoginRateLimiter>(),
                sp.GetRequiredService<IAuditService>());

            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            return controller;
        }

        public async Task<ApplicationUser> AddUserAsync(string email, bool isActive = true)
        {
            using var scope = Provider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, IsActive = isActive };
            var created = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            return user;
        }

        /// <summary>Sample App with Editors (level 2) and Viewers (level 1), plus a group in another app.</summary>
        public async Task SeedGroupsAsync(ApplicationUser user)
        {
            using var scope = Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();

            var sample = new TenantApp { Name = AppName };
            var other = new TenantApp { Name = "Other App" };
            db.TenantApps.AddRange(sample, other);
            await db.SaveChangesAsync();

            var editors = new Group { Name = "Editors", PowerLevel = 2, TenantAppId = sample.Id };
            var viewers = new Group { Name = "Viewers", PowerLevel = 1, TenantAppId = sample.Id };
            var elsewhere = new Group { Name = "Admins", PowerLevel = 9, TenantAppId = other.Id };
            db.Groups.AddRange(editors, viewers, elsewhere);
            await db.SaveChangesAsync();

            db.UserGroups.AddRange(
                new UserGroup { UserId = user.Id, GroupId = editors.Id },
                new UserGroup { UserId = user.Id, GroupId = viewers.Id },
                new UserGroup { UserId = user.Id, GroupId = elsewhere.Id });
            await db.SaveChangesAsync();
        }

        public List<AuditLog> AuditLogs()
        {
            using var scope = Provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<SsoDbContext>()
                .AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToList();
        }

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private static LoginViewModel Form(string email, string password, string? returnUrl = ValidReturnUrl) =>
        new() { Email = email, Password = password, ReturnUrl = returnUrl };

    private static string[] ErrorsOf(Controller controller) =>
        controller.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToArray();

    // ---------------------------------------------------------------- valid login

    [Fact]
    public async Task ValidLogin_RedirectsToReturnUrl_WithSignedJwtCarryingClaims()
    {
        await using var h = new Harness(nameof(ValidLogin_RedirectsToReturnUrl_WithSignedJwtCarryingClaims));
        var user = await h.AddUserAsync("ana@example.com");
        await h.SeedGroupsAsync(user);

        var result = await h.NewController().Login(Form("ana@example.com", Password));

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.StartsWith(ValidReturnUrl + "?token=", redirect.Url);

        var token = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query)["token"].ToString();

        // Signature, issuer, audience and lifetime all check out against the shared key.
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "SSOGateway",
            ValidAudience = "SSOClientApps",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
            ValidateLifetime = true
        });
        Assert.True(validation.IsValid, validation.Exception?.Message);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        Assert.Equal(user.Id, jwt.Subject);
        Assert.Equal("ana@example.com", jwt.Claims.Single(c => c.Type == "email").Value);
        Assert.Equal(AppName, jwt.Claims.Single(c => c.Type == "tenant_app").Value);

        // Only groups from the app being signed in to, with levels in matching order.
        Assert.Equal(new[] { "Editors", "Viewers" }, jwt.Claims.Where(c => c.Type == "groups").Select(c => c.Value));
        Assert.Equal(new[] { "2", "1" }, jwt.Claims.Where(c => c.Type == "levels").Select(c => c.Value));

        Assert.Equal(TimeSpan.FromHours(9), jwt.ValidTo - jwt.IssuedAt);

        var logs = h.AuditLogs();
        var success = Assert.Single(logs, l => l.Action == "LoginSuccess");
        Assert.Equal(user.Id, success.UserId);
        Assert.NotNull(success.TenantAppId);
    }

    [Fact]
    public async Task ValidLogin_SetsNoStoreAndNoReferrerHeaders_AndRecordsLastLogin()
    {
        await using var h = new Harness(nameof(ValidLogin_SetsNoStoreAndNoReferrerHeaders_AndRecordsLastLogin));
        var user = await h.AddUserAsync("ana@example.com");
        var controller = h.NewController();

        await controller.Login(Form("ana@example.com", Password));

        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-referrer", controller.Response.Headers["Referrer-Policy"].ToString());

        using var scope = h.Provider.CreateScope();
        var reloaded = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.Id);
        Assert.NotNull(reloaded!.LastLoginAt);
    }

    [Fact]
    public async Task ValidLogin_PreservesExistingQueryOnReturnUrl()
    {
        await using var h = new Harness(nameof(ValidLogin_PreservesExistingQueryOnReturnUrl));
        await h.AddUserAsync("ana@example.com");

        var result = await h.NewController().Login(Form("ana@example.com", Password, ValidReturnUrl + "?state=abc"));

        var redirect = Assert.IsType<RedirectResult>(result);
        var query = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query);
        Assert.Equal("abc", query["state"]);
        Assert.False(string.IsNullOrEmpty(query["token"]));
    }

    // ---------------------------------------------------------------- invalid credentials

    [Fact]
    public async Task WrongPassword_IsRejected_WithoutToken_AndLogged()
    {
        await using var h = new Harness(nameof(WrongPassword_IsRejected_WithoutToken_AndLogged));
        var user = await h.AddUserAsync("ana@example.com");

        var result = await h.NewController().Login(Form("ana@example.com", "wrong-password"));

        Assert.IsType<ViewResult>(result);
        var log = Assert.Single(h.AuditLogs());
        Assert.Equal("LoginFailed", log.Action);
        Assert.Equal("Invalid email or password", log.Reason);
        Assert.Equal(user.Id, log.UserId);
        Assert.Equal("203.0.113.5", log.IpAddress);
    }

    [Fact]
    public async Task UnknownEmail_GetsSameMessageAsWrongPassword()
    {
        await using var h = new Harness(nameof(UnknownEmail_GetsSameMessageAsWrongPassword));
        await h.AddUserAsync("ana@example.com");

        var wrongPassword = h.NewController();
        await wrongPassword.Login(Form("ana@example.com", "nope"));

        var unknownEmail = h.NewController();
        var result = await unknownEmail.Login(Form("ghost@example.com", "nope"));

        Assert.IsType<ViewResult>(result);
        Assert.Equal(ErrorsOf(wrongPassword), ErrorsOf(unknownEmail));
        Assert.Contains("Invalid email or password.", ErrorsOf(unknownEmail));

        var unknownLog = h.AuditLogs().Last();
        Assert.Equal("LoginFailed", unknownLog.Action);
        Assert.Equal("ghost@example.com", unknownLog.Email);
        Assert.Null(unknownLog.UserId);
    }

    // ---------------------------------------------------------------- inactive account

    [Fact]
    public async Task InactiveAccount_WithCorrectPassword_IsRejectedAsSuspended()
    {
        await using var h = new Harness(nameof(InactiveAccount_WithCorrectPassword_IsRejectedAsSuspended));
        await h.AddUserAsync("off@example.com", isActive: false);
        var controller = h.NewController();

        var result = await controller.Login(Form("off@example.com", Password));

        Assert.IsType<ViewResult>(result);
        Assert.Contains(AuthenticationMessages.AccountSuspended, ErrorsOf(controller));
        var log = Assert.Single(h.AuditLogs());
        Assert.Equal("LoginFailed", log.Action);
        Assert.Equal("Account inactive", log.Reason);
    }

    [Fact]
    public async Task InactiveAccount_WithWrongPassword_DoesNotRevealSuspension()
    {
        await using var h = new Harness(nameof(InactiveAccount_WithWrongPassword_DoesNotRevealSuspension));
        await h.AddUserAsync("off@example.com", isActive: false);
        var controller = h.NewController();

        await controller.Login(Form("off@example.com", "wrong"));

        Assert.DoesNotContain(AuthenticationMessages.AccountSuspended, ErrorsOf(controller));
        Assert.Contains("Invalid email or password.", ErrorsOf(controller));
    }

    // ---------------------------------------------------------------- returnUrl

    [Fact]
    public async Task Get_WithApprovedReturnUrl_ShowsFormWithHiddenReturnUrl()
    {
        await using var h = new Harness(nameof(Get_WithApprovedReturnUrl_ShowsFormWithHiddenReturnUrl));

        var result = h.NewController().Login(ValidReturnUrl);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LoginViewModel>(view.Model);
        Assert.Equal(ValidReturnUrl, model.ReturnUrl);
        Assert.Equal(AppName, model.AppName);
        await Task.CompletedTask;
    }

    public static TheoryData<string?> BlockedReturnUrls => new()
    {
        null,
        "",
        "https://evil.example.com/callback",              // not registered
        "https://example.com.evil.com/callback",          // prefix trick
        "https://example.com@evil.com/callback",          // userinfo trick
        "https://user@example.com/callback",              // userinfo, even on the right host
        "https://example.com/other",                      // right host, wrong path
        "http://example.com/callback",                    // wrong scheme
        "https://example.com:8443/callback",              // wrong port
        "//evil.com/callback",                            // protocol-relative
        "/callback",                                      // relative
        "javascript:alert(1)",
        "https://example.com/callback#fragment",
        "https://example.com/callback?token=attacker",    // pre-seeded token
    };

    [Theory]
    [MemberData(nameof(BlockedReturnUrls))]
    public async Task Get_WithUnapprovedReturnUrl_ShowsErrorInsteadOfForm(string? returnUrl)
    {
        await using var h = new Harness("get-" + Guid.NewGuid());

        var result = h.NewController().Login(returnUrl);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("InvalidReturnUrl", view.ViewName);
        Assert.Equal(StatusCodes.Status400BadRequest, view.StatusCode);
        await Task.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(BlockedReturnUrls))]
    public async Task Post_WithUnapprovedReturnUrl_IsBlocked_BeforeCredentialsAreChecked(string? returnUrl)
    {
        await using var h = new Harness("post-" + Guid.NewGuid());
        await h.AddUserAsync("ana@example.com");

        // Correct credentials, bad destination: still no token.
        var result = await h.NewController().Login(Form("ana@example.com", Password, returnUrl));

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("InvalidReturnUrl", view.ViewName);
        Assert.DoesNotContain(h.AuditLogs(), l => l.Action == "LoginSuccess");
        Assert.Contains(h.AuditLogs(), l => l.Action == "LoginBlocked");

        using var scope = h.Provider.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("ana@example.com");
        Assert.Null(user!.LastLoginAt);
    }

    [Fact]
    public async Task DisabledApp_IsBlocked()
    {
        await using var h = new Harness(nameof(DisabledApp_IsBlocked));
        h.Registry.Apps.Add(new ExternalApp { Id = 2, Name = "Off App", ReturnUrl = "https://off.example.com/cb", IsEnabled = false });

        var result = h.NewController().Login("https://off.example.com/cb");

        Assert.Equal("InvalidReturnUrl", Assert.IsType<ViewResult>(result).ViewName);
        await Task.CompletedTask;
    }

    // ---------------------------------------------------------------- rate limiting

    [Fact]
    public async Task RateLimit_BlocksSixthAttempt_EvenWithCorrectPassword_ThenRecoversAfterWindow()
    {
        await using var h = new Harness(nameof(RateLimit_BlocksSixthAttempt_EvenWithCorrectPassword_ThenRecoversAfterWindow));
        await h.AddUserAsync("ana@example.com");

        for (var i = 0; i < 5; i++)
        {
            var attempt = h.NewController();
            var failed = await attempt.Login(Form("ana@example.com", "wrong"));
            Assert.IsType<ViewResult>(failed);
            Assert.Contains("Invalid email or password.", ErrorsOf(attempt));
        }

        var blocked = h.NewController();
        var blockedResult = await blocked.Login(Form("ana@example.com", Password));

        Assert.IsType<ViewResult>(blockedResult);
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
        Assert.True(int.Parse(blocked.Response.Headers.RetryAfter.ToString()) > 0);
        Assert.Contains(ErrorsOf(blocked), e => e.StartsWith("Too many failed sign-in attempts"));
        Assert.Contains(h.AuditLogs(), l => l.Reason == "Rate limited");
        Assert.DoesNotContain(h.AuditLogs(), l => l.Action == "LoginSuccess");

        h.Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        var recovered = await h.NewController().Login(Form("ana@example.com", Password));
        Assert.IsType<RedirectResult>(recovered);
    }

    [Fact]
    public async Task RateLimit_IsPerAccount_OtherUsersOnSameIpAreNotBlocked()
    {
        await using var h = new Harness(nameof(RateLimit_IsPerAccount_OtherUsersOnSameIpAreNotBlocked));
        await h.AddUserAsync("ana@example.com");
        await h.AddUserAsync("ben@example.com");

        for (var i = 0; i < 6; i++)
        {
            await h.NewController().Login(Form("ana@example.com", "wrong"));
        }

        var ben = await h.NewController().Login(Form("ben@example.com", Password));

        Assert.IsType<RedirectResult>(ben);
    }

    [Fact]
    public async Task RateLimit_SuccessfulLogins_DoNotCount()
    {
        await using var h = new Harness(nameof(RateLimit_SuccessfulLogins_DoNotCount));
        await h.AddUserAsync("ana@example.com");

        for (var i = 0; i < 8; i++)
        {
            var result = await h.NewController().Login(Form("ana@example.com", Password));
            Assert.IsType<RedirectResult>(result);
        }
    }

    // ---------------------------------------------------------------- CORS origin check

    [Fact]
    public void Registry_ApprovesOnlyOriginsOfEnabledApps()
    {
        var registry = new InMemoryExternalAppRegistry();
        registry.Apps.Add(new ExternalApp { Id = 2, Name = "Off", ReturnUrl = "https://off.example.com/cb", IsEnabled = false });

        Assert.True(registry.IsOriginApproved("https://example.com"));
        Assert.False(registry.IsOriginApproved("https://evil.com"));
        Assert.False(registry.IsOriginApproved("https://off.example.com"));
        Assert.False(registry.IsOriginApproved("null"));
        Assert.False(registry.IsOriginApproved(null));
    }
}
