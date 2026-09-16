using Data;
using Gateway.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Controllers;

[Authorize(Roles = SeedData.AdminRole)]
[Route("Admin/AuditLogs")]
public class AuditController : Controller
{
    private const int MaxPageSize = 100;
    private readonly SsoDbContext _db;
    public AuditController(SsoDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, int pageSize = 20, DateTime? from = null, DateTime? to = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        IQueryable<AuditLog> query = _db.AuditLogs.AsNoTracking().Include(x => x.User)
            .OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Id);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(x => x.Timestamp >= fromUtc);
        }
        if (to.HasValue)
        {
            var toExclusiveUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(x => x.Timestamp < toExclusiveUtc);
        }

        var totalCount = await query.CountAsync();
        var logs = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AuditLogListItemViewModel
            {
                Id = x.Id, Action = x.Action, Details = x.Details, Timestamp = x.Timestamp,
                IpAddress = x.IpAddress, UserId = x.UserId, Email = x.User != null ? x.User.Email : null
            }).ToListAsync();

        return View(new AuditLogListViewModel { Logs = logs, Page = page, PageSize = pageSize, TotalCount = totalCount, From = from, To = to });
    }
}
