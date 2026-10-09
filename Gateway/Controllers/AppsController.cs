using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers;

/// <summary>
/// Kept so old /Apps links still work. App registration now lives in Admin > Apps
/// (TenantAppsController), which stores apps in the database the login gateway reads.
/// </summary>
public class AppsController : Controller
{
    public IActionResult Index() =>
        RedirectToAction("Index", "TenantApps", new { area = "Admin" });
}
