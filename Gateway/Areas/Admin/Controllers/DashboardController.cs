using Data;
using Gateway.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Areas.Admin.Controllers
{
    public class DashboardController : AdminBaseController
    {
        private readonly SsoDbContext _db;

        public DashboardController(SsoDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var model = new DashboardViewModel
            {
                TotalUsers = await _db.Users.CountAsync(),
                ActiveUsers = await _db.Users.CountAsync(u => u.IsActive),
                TotalApps = await _db.TenantApps.CountAsync(),
                TotalGroups = await _db.Groups.CountAsync()
            };

            return View(model);
        }
    }
}
