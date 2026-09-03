using Gateway.Models;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Controllers
{
    public class AppsController : Controller
    {
        private static readonly List<ExternalApp> Apps = new()
        {
            new ExternalApp
            {
                Id = 1,
                Name = "Sample App",
                ReturnUrl = "https://example.com/callback",
                IsEnabled = true
            }
        };

        // GET: /Apps
        public IActionResult Index()
        {
            return View(Apps);
        }

        // GET: /Apps/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Apps/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(ExternalApp app)
        {
            // Check if app name already exists
            if (Apps.Any(x =>
                x.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError("Name", "An app with this name already exists.");
            }

            if (!ModelState.IsValid)
            {
                return View(app);
            }

            app.Id = Apps.Count == 0 ? 1 : Apps.Max(x => x.Id) + 1;
            app.IsEnabled = true;

            Apps.Add(app);

            return RedirectToAction(nameof(Index));
        }

        // GET: /Apps/Edit/1
        public IActionResult Edit(int id)
        {
            var app = Apps.FirstOrDefault(x => x.Id == id);

            if (app == null)
            {
                return NotFound();
            }

            return View(app);
        }

        // POST: /Apps/Edit/1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, ExternalApp app)
        {
            var existingApp = Apps.FirstOrDefault(x => x.Id == id);

            if (existingApp == null)
            {
                return NotFound();
            }

            // Check duplicate name, excluding current app
            if (Apps.Any(x =>
                x.Id != id &&
                x.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(
                    "Name",
                    "An app with this name already exists."
                );
            }

            if (!ModelState.IsValid)
            {
                return View(app);
            }

            existingApp.Name = app.Name;
            existingApp.ReturnUrl = app.ReturnUrl;

            return RedirectToAction(nameof(Index));
        }

        // GET: /Apps/Delete/1
        public IActionResult Delete(int id)
        {
            var app = Apps.FirstOrDefault(x => x.Id == id);

            if (app == null)
            {
                return NotFound();
            }

            return View(app);
        }

        // POST: /Apps/Delete/1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            var app = Apps.FirstOrDefault(x => x.Id == id);

            if (app == null)
            {
                return NotFound();
            }

            Apps.Remove(app);

            return RedirectToAction(nameof(Index));
        }

        // POST: /Apps/Toggle
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Toggle(int id)
        {
            var app = Apps.FirstOrDefault(x => x.Id == id);

            if (app == null)
            {
                return NotFound();
            }

            app.IsEnabled = !app.IsEnabled;

            return RedirectToAction(nameof(Index));
        }
    }
}