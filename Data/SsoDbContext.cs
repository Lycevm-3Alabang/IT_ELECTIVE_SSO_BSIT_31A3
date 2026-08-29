using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Data;

public class SsoDbContext : IdentityDbContext<IdentityUser>
{
    public SsoDbContext(DbContextOptions options) : base(options)
    {
    }

    protected SsoDbContext()
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TenantApp>(t =>
        {
            t.Property(a => a.Name)
            .IsRequired()
            .HasColumnName("Name")
            .HasMaxLength(100);


            t.HasKey(a => a.Id);
        });
    }
}
