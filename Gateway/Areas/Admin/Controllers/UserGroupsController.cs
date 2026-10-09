using System.Security.Claims;
using Data;
using Gateway.Areas.Admin.Models.Users;
using Gateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;

namespace Gateway.Areas.Admin.Controllers;

/// <summary>
/// Admin endpoints for a user's group memberships.
///
///   GET    /Admin/Users/{userId}/Groups             list the user's groups (JSON)
///   POST   /Admin/Users/{userId}/Groups             assign the user to a group (form field: groupId)
///   DELETE /Admin/Users/{userId}/Groups/{groupId}   remove the user from a group
///
/// A user can be in any number of groups, in the same app or across different
/// apps, but only once per group. Each group's power level tells the external
/// app what that membership allows.
/// </summary>
[Route("Admin/Users/{userId}/Groups")]
public class UserGroupsController : AdminBaseController
{
    private readonly SsoDbContext _db;
    private readonly IAuditService _auditService;

    public UserGroupsController(SsoDbContext db, IAuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string userId)
    {
        var user = await FindUserAsync(userId);
        if (user is null) return NotFound();

        return Json(new UserGroupListResponse
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            Groups = await _db.GetUserGroupsAsync(user.Id)
        });
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(string userId, int groupId)
    {
        var user = await FindUserAsync(userId);
        if (user is null) return NotFound();

        if (groupId <= 0)
        {
            return Failure(user.Id, StatusCodes.Status400BadRequest, "Select a group to assign.");
        }

        var group = await _db.Groups.AsNoTracking().Include(g => g.TenantApp)
            .FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null)
        {
            return Failure(user.Id, StatusCodes.Status404NotFound, "That group no longer exists.");
        }

        var duplicate = $"{user.Email} is already in group '{group.Name}'.";
        if (await IsMemberAsync(user.Id, groupId))
        {
            return Failure(user.Id, StatusCodes.Status409Conflict, duplicate);
        }

        var link = new UserGroup { UserId = user.Id, GroupId = groupId };
        _db.UserGroups.Add(link);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two admins assigning at the same moment: the (UserId, GroupId)
            // primary key stops the second insert. Report it as the duplicate it is.
            _db.Entry(link).State = EntityState.Detached;
            if (!await IsMemberAsync(user.Id, groupId))
            {
                throw;
            }

            return Failure(user.Id, StatusCodes.Status409Conflict, duplicate);
        }

        await _auditService.LogAction(
            "AssignGroup",
            $"Admin added {user.Email} to group '{group.Name}' (power level {group.PowerLevel}).",
            CurrentAdminId(),
            ClientIp());

        var message = $"{user.Email} was added to '{group.Name}'.";
        return Success(user.Id, message, new
        {
            success = true,
            message,
            group = new UserGroupItemViewModel
            {
                GroupId = group.Id,
                GroupName = group.Name,
                AppId = group.TenantAppId,
                AppName = group.TenantApp.Name,
                PowerLevel = group.PowerLevel
            }
        });
    }

    [HttpDelete("{groupId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign(string userId, int groupId)
    {
        var link = await _db.UserGroups
            .Include(ug => ug.User)
            .Include(ug => ug.Group)
            .FirstOrDefaultAsync(ug => ug.UserId == userId && ug.GroupId == groupId);
        if (link is null) return NotFound();   // unknown user, unknown group, or not a member

        var email = link.User.Email;
        var groupName = link.Group.Name;

        _db.UserGroups.Remove(link);
        await _db.SaveChangesAsync();

        await _auditService.LogAction(
            "UnassignGroup",
            $"Admin removed {email} from group '{groupName}'.",
            CurrentAdminId(),
            ClientIp());

        var message = $"{email} was removed from '{groupName}'.";
        return Success(userId, message, new { success = true, message, groupId });
    }

    private Task<ApplicationUser?> FindUserAsync(string userId) =>
        _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

    private Task<bool> IsMemberAsync(string userId, int groupId) =>
        _db.UserGroups.AnyAsync(ug => ug.UserId == userId && ug.GroupId == groupId);

    /// <summary>AJAX callers get JSON; plain form posts go back to the user's Details page.</summary>
    private IActionResult Success(string userId, string message, object json)
    {
        TempData["StatusMessage"] = message;
        return IsAjaxRequest() ? Json(json) : RedirectToDetails(userId);
    }

    private IActionResult Failure(string userId, int statusCode, string message)
    {
        if (IsAjaxRequest())
        {
            return StatusCode(statusCode, new { success = false, message });
        }

        TempData["ErrorMessage"] = message;
        return RedirectToDetails(userId);
    }

    private IActionResult RedirectToDetails(string userId) =>
        RedirectToAction("Details", "Users", new { area = "Admin", id = userId });

    private bool IsAjaxRequest() =>
        HttpContext?.Request.Headers.TryGetValue("X-Requested-With", out var value) == true &&
        string.Equals(value.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    private string? CurrentAdminId() => HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    private string? ClientIp() => HttpContext?.Connection?.RemoteIpAddress?.ToString();
}
