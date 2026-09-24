using System.Security.Cryptography;
using Gateway.Areas.Admin.Models.Users;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Areas.Admin.Controllers
{
    public class UsersController : AdminBaseController
    {
        // TODO: replace with real EF Core-backed storage once Issue 4 is merged.
        private static readonly List<UserRecord> Users = new()
        {
            new UserRecord
            {
                Id = "1",
                Email = "admin@example.com",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                LastLoginAt = DateTime.UtcNow.AddHours(-3),
                Groups = new List<string> { "Administrators" }
            },
            new UserRecord
            {
                Id = "2",
                Email = "jdoe@example.com",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                Groups = new List<string> { "Staff" }
            }
        };

        private static readonly List<AuditLogEntry> AuditLog = new();

        // GET: /Admin/Users
        public IActionResult Index(string? search)
        {
            var query = Users.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(u =>
                    u.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var model = new UserListViewModel
            {
                SearchTerm = search,
                Users = query
                    .OrderBy(u => u.Email)
                    .Select(u => new UserListItemViewModel
                    {
                        Id = u.Id,
                        Email = u.Email,
                        IsActive = u.IsActive,
                        CreatedAt = u.CreatedAt
                    })
                    .ToList()
            };

            return View(model);
        }

        // GET: /Admin/Users/Details/1
        public IActionResult Details(string id)
        {
            var user = Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
            {
                return NotFound();
            }

            var model = new UserDetailsViewModel
            {
                Id = user.Id,
                Email = user.Email,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                Groups = user.Groups,
                RecentActivity = AuditLog
                    .Where(a => a.UserId == id)
                    .OrderByDescending(a => a.Timestamp)
                    .Take(5)
                    .ToList()
            };

            return View(model);
        }

        // GET: /Admin/Users/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Admin/Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(CreateUserViewModel model)
        {
            if (Users.Any(u => u.Email.Equals(model.Email, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(nameof(model.Email), "A user with this email already exists.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = new UserRecord
            {
                Id = (Users.Count == 0 ? 1 : Users.Max(u => int.Parse(u.Id)) + 1).ToString(),
                Email = model.Email,
                TemporaryPassword = model.TemporaryPassword,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            Users.Add(user);
            LogAction(user.Id, "User created");

            TempData["StatusMessage"] = $"User {user.Email} was created.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/ToggleActive/1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleActive(string id)
        {
            var user = Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
            {
                return NotFound();
            }

            user.IsActive = !user.IsActive;
            LogAction(user.Id, user.IsActive ? "User activated" : "User deactivated");

            return Json(new { isActive = user.IsActive });
        }

        // POST: /Admin/Users/Delete/1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(string id)
        {
            var user = Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
            {
                return NotFound();
            }

            // Soft delete: deactivate rather than remove the record.
            user.IsActive = false;
            LogAction(user.Id, "User deleted");

            TempData["StatusMessage"] = $"User {user.Email} was deleted.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/ResetPassword/1
        // Issue 117-122: generates a new temporary password for a user,
        // returns it to the confirmation dialog, and records an audit entry.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetPassword(string id)
        {
            var user = Users.FirstOrDefault(u => u.Id == id);

            if (user == null)
            {
                return NotFound();
            }

            var temporaryPassword = GenerateTemporaryPassword();
            user.TemporaryPassword = temporaryPassword;

            LogAction(user.Id, "Password reset");

            return Json(new
            {
                email = user.Email,
                temporaryPassword,
                resetAt = DateTime.UtcNow.ToLocalTime().ToString("MMM d, yyyy h:mm tt")
            });
        }

        private void LogAction(string userId, string action)
        {
            AuditLog.Add(new AuditLogEntry
            {
                UserId = userId,
                Action = action,
                PerformedBy = User.Identity?.Name ?? "system",
                Timestamp = DateTime.UtcNow
            });
        }

        private static string GenerateTemporaryPassword()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnopqrstuvwxyz";
            const string digits = "23456789";
            const string symbols = "!@#$%^&*";
            const string all = upper + lower + digits + symbols;

            Span<char> password = stackalloc char[12];

            // Guarantee at least one of each character class.
            password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
            password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
            password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
            password[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];

            for (var i = 4; i < password.Length; i++)
            {
                password[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
            }

            // Shuffle so the guaranteed characters aren't always in the same spots.
            for (var i = password.Length - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(i + 1);
                (password[i], password[j]) = (password[j], password[i]);
            }

            return new string(password);
        }
    }
}
