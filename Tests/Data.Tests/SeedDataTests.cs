using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;

namespace Data.Tests;

public class SeedDataTests
{
    private const string AdminEmail = "admin@itelective-sso.local";
    private const string AdminPassword = "ChangeMe!123";

    private static ServiceProvider BuildServiceProvider(string dbName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminSeed:Email"] = AdminEmail,
                ["AdminSeed:Password"] = AdminPassword,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddDbContext<SsoDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<SsoDbContext>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedAdminAsync_CreatesAdmin_WhenMissing()
    {
        await using var provider = BuildServiceProvider(nameof(SeedAdminAsync_CreatesAdmin_WhenMissing));

        await SeedData.SeedAdminAsync(provider);

        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await userManager.FindByEmailAsync(AdminEmail);

        Assert.NotNull(admin);
        Assert.True(admin!.IsActive);
        Assert.True(await userManager.IsInRoleAsync(admin, SeedData.AdminRole));
        Assert.True(await userManager.CheckPasswordAsync(admin, AdminPassword));
    }

    [Fact]
    public async Task SeedAdminAsync_SkipsSeed_WhenAdminAlreadyExists()
    {
        await using var provider = BuildServiceProvider(nameof(SeedAdminAsync_SkipsSeed_WhenAdminAlreadyExists));

        // Run the seed twice; the second run must not create a duplicate
        // or throw because the admin already exists.
        await SeedData.SeedAdminAsync(provider);
        await SeedData.SeedAdminAsync(provider);

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SsoDbContext>();

        var adminCount = dbContext.Users.Count(u => u.Email == AdminEmail);

        Assert.Equal(1, adminCount);
    }
}
