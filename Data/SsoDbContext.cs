using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Data;

public class SsoDbContext : IdentityDbContext<IdentityUser>
{
    public SsoDbContext(DbContextOptions options) : base(options)
    {
    }

    protected SsoDbContext()
    {
    }
}
