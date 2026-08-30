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

    public DbSet<TenantApp> TenantApps => Set<TenantApp>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TenantApp>(t =>
        {
            t.HasKey(a => a.Id);

            t.Property(a => a.Name)
                .IsRequired()
                .HasColumnName("Name")
                .HasMaxLength(100);
        });

        builder.Entity<Group>(g =>
        {
            g.HasKey(x => x.Id);

            g.Property(x => x.Name)
                .IsRequired()
                .HasColumnName("Name")
                .HasMaxLength(100);

            g.HasOne(x => x.TenantApp)
                .WithMany(t => t.Groups)
                .HasForeignKey(x => x.TenantAppId)
                .OnDelete(DeleteBehavior.Cascade); 
        });

        builder.Entity<UserGroup>(ug =>
        {
            ug.HasKey(x => new { x.UserId, x.GroupId });

            ug.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade); 

            ug.HasOne(x => x.Group)
                .WithMany()
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}