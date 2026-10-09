using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models;

namespace Data;

/// <summary>
/// Creates the Day 1 admin account on first run so the system is usable
/// immediately after deployment, without anyone having to sign up.
///
/// Reads the admin's email/password from configuration ("AdminSeed" section)
/// so credentials never get hard-coded into source. The check is idempotent:
/// re-running the seed on every app startup is safe and a no-op once the
/// admin already exists.
/// </summary>
public static class SeedData
{
    public const string AdminRole = "Admin";

    /// <summary>
    /// Entry point called from Program.cs on startup. Resolves its own
    /// scoped services so it can run once, then dispose them.
    /// </summary>
    public static async Task SeedAdminAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(SeedData));

        var adminSection = configuration.GetSection("AdminSeed");
        var email = adminSection["Email"];
        var password = adminSection["Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "AdminSeed:Email or AdminSeed:Password missing from configuration. " +
                "Skipping Day 1 admin seed.");
            return;
        }

        await EnsureAdminRoleAsync(roleManager, logger);
        await EnsureAdminUserAsync(userManager, email, password, logger);
    }

    private static async Task EnsureAdminRoleAsync(RoleManager<IdentityRole> roleManager, ILogger logger)
    {
        if (await roleManager.RoleExistsAsync(AdminRole))
        {
            return;
        }

        var result = await roleManager.CreateAsync(new IdentityRole(AdminRole));
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            logger.LogError("Failed to create the {Role} role: {Errors}", AdminRole, errors);
        }
    }

    private static async Task EnsureAdminUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        ILogger logger)
    {
        // Idempotent check: if the admin already exists, do nothing.
        var existingAdmin = await userManager.FindByEmailAsync(email);
        if (existingAdmin is not null)
        {
            logger.LogInformation("Day 1 admin ({Email}) already exists. Skipping seed.", email);
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var createResult = await userManager.CreateAsync(admin, password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            logger.LogError("Failed to create Day 1 admin ({Email}): {Errors}", email, errors);
            return;
        }

        var roleResult = await userManager.AddToRoleAsync(admin, AdminRole);
        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            logger.LogError("Created Day 1 admin but failed to assign {Role} role: {Errors}", AdminRole, errors);
            return;
        }

        logger.LogInformation("Day 1 admin ({Email}) created.", email);
    }
}
