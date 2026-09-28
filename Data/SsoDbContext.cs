using Microsoft.EntityFrameworkCore;
using Models;

namespace Data;

public class SsoDbContext : DbContext
{
    public SsoDbContext(DbContextOptions<SsoDbContext> options) : base(options)
    {
    }

    // Minimal TenantApps DbSet to satisfy controllers/tests
    public DbSet<TenantApp> TenantApps { get; set; } = null!;
}
