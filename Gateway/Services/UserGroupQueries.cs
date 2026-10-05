using Data;
using Gateway.Areas.Admin.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Services;

/// <summary>
/// Shared queries for a user's group memberships, used by the Details page and
/// the /Admin/Users/{userId}/Groups endpoints so both always agree.
/// </summary>
public static class UserGroupQueries
{
    /// <summary>The user's groups across all apps, ordered by app, then power (0 first), then name.</summary>
    public static Task<List<UserGroupItemViewModel>> GetUserGroupsAsync(this SsoDbContext db, string userId) =>
        db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .OrderBy(ug => ug.Group.TenantApp.Name)
            .ThenBy(ug => ug.Group.PowerLevel)
            .ThenBy(ug => ug.Group.Name)
            .Select(ug => new UserGroupItemViewModel
            {
                GroupId = ug.GroupId,
                GroupName = ug.Group.Name,
                AppId = ug.Group.TenantAppId,
                AppName = ug.Group.TenantApp.Name,
                PowerLevel = ug.Group.PowerLevel
            })
            .ToListAsync();

    /// <summary>Every group the user is NOT in yet, in the same order.</summary>
    public static Task<List<AvailableGroupViewModel>> GetAvailableGroupsAsync(this SsoDbContext db, string userId) =>
        db.Groups.AsNoTracking()
            .Where(g => !db.UserGroups.Any(ug => ug.GroupId == g.Id && ug.UserId == userId))
            .OrderBy(g => g.TenantApp.Name)
            .ThenBy(g => g.PowerLevel)
            .ThenBy(g => g.Name)
            .Select(g => new AvailableGroupViewModel
            {
                GroupId = g.Id,
                GroupName = g.Name,
                AppName = g.TenantApp.Name,
                PowerLevel = g.PowerLevel
            })
            .ToListAsync();
}
