using System.Text;
using Data;
using Gateway.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MockClient;
using Models;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Starts the real gateway (on a throw-away SQLite file) and the mock client side by side,
/// both in memory, and seeds one user who belongs to two groups of the "Mock Client" app
/// and one group of a different app.
/// </summary>
public sealed class SsoFlowFixture : IAsyncLifetime
{
    public const string AppName = "Mock Client";
    public const string UserEmail = "flow.user@example.test";
    public const string UserPassword = "Passw0rd!x";
    public const string SecretKey = "integration-test-secret-key-at-least-32-bytes";
    public const string Issuer = "SSOGateway";
    public const string Audience = "SSOClientApps";

    public const string GatewayHost = "http://gateway.test";
    public const string MockClientHost = "http://mockclient.test";
    public static string CallbackUrl => MockClientHost + "/callback";

    public static readonly string[] ExpectedGroups = { "Mock Client-Editors", "Mock Client-Viewers" };
    public static readonly int[] ExpectedLevels = { 2, 5 };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sso-integration-{Guid.NewGuid():N}.db");
    private GatewayFactory _gateway = null!;
    private MockClientFactory _mockClient = null!;

    public HttpClient NewGatewayClient() =>
        _gateway.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(GatewayHost)
        });

    public HttpClient NewMockClient() =>
        _mockClient.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(MockClientHost)
        });

    public async Task InitializeAsync()
    {
        _gateway = new GatewayFactory(new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
            ["JwtSettings:Issuer"] = Issuer,
            ["JwtSettings:Audience"] = Audience,
            ["JwtSettings:SecretKey"] = SecretKey,
            ["JwtSettings:ExpiryHours"] = "1"
        });

        _mockClient = new MockClientFactory(new Dictionary<string, string>
        {
            ["Gateway:BaseUrl"] = GatewayHost,
            ["MockClient:AppName"] = AppName,
            ["JwtSettings:Issuer"] = Issuer,
            ["JwtSettings:Audience"] = Audience,
            ["JwtSettings:SecretKey"] = SecretKey
        });

        await SeedAsync();
    }

    public Task DisposeAsync()
    {
        _mockClient.Dispose();
        _gateway.Dispose();

        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try { File.Delete(_dbPath + suffix); }
            catch (IOException) { /* a pooled connection may still hold it; the temp folder is cleaned up eventually */ }
        }

        return Task.CompletedTask;
    }

    private async Task SeedAsync()
    {
        // Resolving Services starts the gateway, which creates the database and seeds the admin.
        using var scope = _gateway.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var mockApp = new TenantApp { Name = AppName, ReturnUrl = CallbackUrl, IsEnabled = true };
        var otherApp = new TenantApp { Name = "Other App", ReturnUrl = "http://other.test/callback", IsEnabled = true };
        db.TenantApps.AddRange(mockApp, otherApp);
        await db.SaveChangesAsync();

        var editors = new Group { Name = GroupRules.BuildName(AppName, "Editors"), PowerLevel = 2, TenantAppId = mockApp.Id };
        var viewers = new Group { Name = GroupRules.BuildName(AppName, "Viewers"), PowerLevel = 5, TenantAppId = mockApp.Id };
        var foreign = new Group { Name = GroupRules.BuildName("Other App", "Admins"), PowerLevel = 0, TenantAppId = otherApp.Id };
        db.Groups.AddRange(editors, viewers, foreign);
        await db.SaveChangesAsync();

        var user = new ApplicationUser { UserName = UserEmail, Email = UserEmail, EmailConfirmed = true, IsActive = true };
        var created = await users.CreateAsync(user, UserPassword);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create the test user: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        db.UserGroups.AddRange(
            new UserGroup { UserId = user.Id, GroupId = editors.Id },
            new UserGroup { UserId = user.Id, GroupId = viewers.Id },
            new UserGroup { UserId = user.Id, GroupId = foreign.Id });
        await db.SaveChangesAsync();
    }

    /// <summary>A token signed like the gateway's, but with a lifetime and key the test controls.</summary>
    public static string MintToken(
        string tenantApp = AppName,
        TimeSpan? expiresIn = null,
        string key = SecretKey)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = now.AddHours(-3),
            IssuedAt = now.AddHours(-3),
            Expires = now.Add(expiresIn ?? TimeSpan.FromHours(1)),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "test-user-id",
                ["email"] = UserEmail,
                ["tenant_app"] = tenantApp,
                ["groups"] = ExpectedGroups,
                ["levels"] = ExpectedLevels
            }
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // The Gateway's generated Program class is internal, so each factory points at a public type
    // from the same assembly instead (WebApplicationFactory only uses it to find the assembly).
    private sealed class GatewayFactory(IReadOnlyDictionary<string, string> settings)
        : WebApplicationFactory<HomeController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }

    private sealed class MockClientFactory(IReadOnlyDictionary<string, string> settings)
        : WebApplicationFactory<AssemblyMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
