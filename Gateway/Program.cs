using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Models;
using Gateway.Services;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using System.Text;

const string ExternalClientAppsCors = "ExternalClientApps";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IAuditService, AuditService>();

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.Configure<LoginRateLimitSettings>(builder.Configuration.GetSection("LoginRateLimit"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IExternalAppRegistry, InMemoryExternalAppRegistry>();
builder.Services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// Only origins of enabled, registered apps get CORS headers. No credentials are allowed.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IExternalAppRegistry>((options, registry) =>
    options.AddPolicy(ExternalClientAppsCors, policy => policy
        .SetIsOriginAllowed(registry.IsOriginApproved)
        .WithMethods("GET", "POST")
        .WithHeaders("Authorization", "Content-Type")));

builder.Services.AddDbContext<SsoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<SsoDbContext>()
    .AddDefaultTokenProviders();

// Use the active-aware SignInManager so inactive accounts cannot log in.
builder.Services.AddScoped<SignInManager<ApplicationUser>, ActiveUserSignInManager>();

var app = builder.Build();

// Fail at startup, not on the first login, if the signing key is too short for HS256.
var jwtKey = app.Services.GetRequiredService<IOptions<JwtSettings>>().Value.SecretKey;
if (Encoding.UTF8.GetByteCount(jwtKey ?? string.Empty) < 32)
{
    throw new InvalidOperationException("JwtSettings:SecretKey must be at least 32 bytes long.");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await SeedData.SeedAdminAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(ExternalClientAppsCors);

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();