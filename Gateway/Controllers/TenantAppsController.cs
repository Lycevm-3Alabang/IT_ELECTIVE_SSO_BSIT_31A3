using Gateway.Models;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/[controller]/[action]")]
    public class TenantAppsController : Controller
    {
        private static readonly List<ExternalApp> _apps = new()
        {
            new ExternalApp { Id = 1, Name = "Finance Portal", ReturnUrl = "https://finance.local/sso/callback", IsEnabled = true },
            new ExternalApp { Id = 2, Name = "HR Dashboard", ReturnUrl = "https://hr.local/auth/callback", IsEnabled = false }
        };

        // Task #108: App list page
        [HttpGet]
        public IActionResult Index()
        {
            return View(_apps);
        }

        // Task #109: Create app form (GET)
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // Task #109 & #112: Create app form with validation (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(ExternalApp app)
        {
            if (_apps.Any(a => a.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError("Name", "An application with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                return View(app);
            }

            app.Id = _apps.Any() ? _apps.Max(a => a.Id) + 1 : 1;
            _apps.Add(app);
            TempData["SuccessMessage"] = "Application registered successfully.";
            return RedirectToAction(nameof(Index));
        }

        // Task #110: Edit app form (GET)
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var app = _apps.FirstOrDefault(a => a.Id == id);
            if (app == null) return NotFound();

            return View(app);
        }

        // Task #110 & #112: Edit app form with validation (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, ExternalApp app)
        {
            var existingApp = _apps.FirstOrDefault(a => a.Id == id);
            if (existingApp == null) return NotFound();

            if (_apps.Any(a => a.Id != id && a.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError("Name", "An application with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                return View(app);
            }

            existingApp.Name = app.Name;
            existingApp.ReturnUrl = app.ReturnUrl;
            existingApp.IsEnabled = app.IsEnabled;

            TempData["SuccessMessage"] = "Application updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // Task #113: Toggle Enable/Disable
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleStatus(int id)
        {
            var app = _apps.FirstOrDefault(a => a.Id == id);
            if (app != null)
            {
                app.IsEnabled = !app.IsEnabled;
                TempData["SuccessMessage"] = $"Application '{(app.IsEnabled ? "enabled" : "disabled")}' successfully.";
            }
            return RedirectToAction(nameof(Index));
        }

        // Task #111: Delete Action via Modal (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var app = _apps.FirstOrDefault(a => a.Id == id);
            if (app != null)
            {
                _apps.Remove(app);
                TempData["SuccessMessage"] = "Application deleted successfully.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}