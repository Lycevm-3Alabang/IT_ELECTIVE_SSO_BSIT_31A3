using Gateway.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Areas.Admin.Controllers
{
    public class DashboardController : AdminBaseController
    {
        public IActionResult Index()
        {
            // TODO: replace with real EF Core queries once
            // Issue 4 (Users) and Issue 8 (TenantApps) are merged.
            var model = new DashboardViewModel
            {
                TotalUsers = 0,
                ActiveUsers = 0,
                TotalApps = 0,
                TotalGroups = 0
            };

            return View(model);
        }
    }
}