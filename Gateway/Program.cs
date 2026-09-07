using Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<SsoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Reasonable defaults for a school-project SSO; tighten as needed.
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<SsoDbContext>()
    .AddDefaultTokenProviders();

var app = builder.Build();

// Ensure the SQLite schema (AspNetRoles, AspNetUsers, TenantApps, etc.)
// exists before anything tries to query it. The project has no EF Core
// migrations yet, so without this the database file is created empty and
// every query - including the Day 1 admin seed below - fails with
// "SQLite Error 1: 'no such table: AspNetRoles'".
//
// EnsureCreatedAsync is idempotent: it's a no-op once the tables exist.
// If/when real EF Core migrations are added to the Data project, replace
// this call with `await db.Database.MigrateAsync();` instead, since
// EnsureCreated and migrations should not be mixed.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SsoDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Create the Day 1 admin account on first run (idempotent - safe to run
// on every startup, see Data/SeedData.cs for Issue 3).
await SeedData.SeedAdminAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();