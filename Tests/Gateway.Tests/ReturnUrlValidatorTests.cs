using Data;
using Gateway.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models;
using Xunit;

namespace Gateway.Tests;

public class ReturnUrlValidatorTests
{
    private static ServiceProvider BuildServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<SsoDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IReturnUrlValidator, ReturnUrlValidator>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ValidateAsync_WithRegisteredEnabledUrl_ReturnsMatchingApp()
    {
        await using var provider = BuildServiceProvider(nameof(ValidateAsync_WithRegisteredEnabledUrl_ReturnsMatchingApp));
        var db = provider.GetRequiredService<SsoDbContext>();

        db.TenantApps.Add(new TenantApp
        {
            Name = "Sample App",
            ReturnUrl = "https://sample-app.example.com/callback",
            IsEnabled = true
        });
        await db.SaveChangesAsync();

        var validator = provider.GetRequiredService<IReturnUrlValidator>();

        var result = await validator.ValidateAsync("https://sample-app.example.com/callback");

        Assert.NotNull(result);
        Assert.Equal("Sample App", result!.Name);
    }

    [Fact]
    public async Task ValidateAsync_IsCaseInsensitiveAndTrimsWhitespace()
    {
        await using var provider = BuildServiceProvider(nameof(ValidateAsync_IsCaseInsensitiveAndTrimsWhitespace));
        var db = provider.GetRequiredService<SsoDbContext>();

        db.TenantApps.Add(new TenantApp
        {
            Name = "Mixed Case App",
            ReturnUrl = "https://Mixed-Case.example.com/Callback",
            IsEnabled = true
        });
        await db.SaveChangesAsync();

        var validator = provider.GetRequiredService<IReturnUrlValidator>();

        var result = await validator.ValidateAsync("  https://mixed-case.example.com/callback  ");

        Assert.NotNull(result);
        Assert.Equal("Mixed Case App", result!.Name);
    }

    [Fact]
    public async Task ValidateAsync_WithUnregisteredUrl_ReturnsNull()
    {
        await using var provider = BuildServiceProvider(nameof(ValidateAsync_WithUnregisteredUrl_ReturnsNull));
        var db = provider.GetRequiredService<SsoDbContext>();

        db.TenantApps.Add(new TenantApp
        {
            Name = "Registered App",
            ReturnUrl = "https://registered.example.com/callback",
            IsEnabled = true
        });
        await db.SaveChangesAsync();

        var validator = provider.GetRequiredService<IReturnUrlValidator>();

        var result = await validator.ValidateAsync("https://not-registered.example.com/callback");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_WithDisabledApp_ReturnsNull()
    {
        await using var provider = BuildServiceProvider(nameof(ValidateAsync_WithDisabledApp_ReturnsNull));
        var db = provider.GetRequiredService<SsoDbContext>();

        db.TenantApps.Add(new TenantApp
        {
            Name = "Disabled App",
            ReturnUrl = "https://disabled.example.com/callback",
            IsEnabled = false
        });
        await db.SaveChangesAsync();

        var validator = provider.GetRequiredService<IReturnUrlValidator>();

        var result = await validator.ValidateAsync("https://disabled.example.com/callback");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_WithNullOrEmptyUrl_ReturnsNullAndDoesNotLog()
    {
        await using var provider = BuildServiceProvider(nameof(ValidateAsync_WithNullOrEmptyUrl_ReturnsNullAndDoesNotLog));
        var validator = provider.GetRequiredService<IReturnUrlValidator>();

        var nullResult = await validator.ValidateAsync(null);
        var emptyResult = await validator.ValidateAsync("   ");

        Assert.Null(nullResult);
        Assert.Null(emptyResult);

        var db = provider.GetRequiredService<SsoDbContext>();
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task ValidateAsync_WithUnregisteredUrl_LogsInvalidAttemptToAuditLogs()
    {
        await using var provider = BuildServiceProvider(nameof(ValidateAsync_WithUnregisteredUrl_LogsInvalidAttemptToAuditLogs));
        var validator = provider.GetRequiredService<IReturnUrlValidator>();

        const string suspiciousUrl = "https://malicious.example.com/phish";

        var result = await validator.ValidateAsync(suspiciousUrl, ipAddress: "203.0.113.5");

        Assert.Null(result);

        var db = provider.GetRequiredService<SsoDbContext>();
        var log = await db.AuditLogs.SingleAsync(x => x.Action == "InvalidReturnUrl");

        Assert.Contains(suspiciousUrl, log.Details);
        Assert.Equal("203.0.113.5", log.IpAddress);
    }

    [Fact]
    public void ExtractReturnUrl_ReadsValueFromQueryString()
    {
        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["returnUrl"] = "https://app.example.com/callback"
            });

        var validator = new ReturnUrlValidator(
            null!,
            null!);

        // ExtractReturnUrl doesn't touch the db or audit service, so it's
        // safe to call on an instance built with null dependencies.
        var extracted = validator.ExtractReturnUrl(query);

        Assert.Equal("https://app.example.com/callback", extracted);
    }

    [Fact]
    public void ExtractReturnUrl_WithMissingKey_ReturnsNull()
    {
        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());

        var validator = new ReturnUrlValidator(null!, null!);

        var extracted = validator.ExtractReturnUrl(query);

        Assert.Null(extracted);
    }
}
