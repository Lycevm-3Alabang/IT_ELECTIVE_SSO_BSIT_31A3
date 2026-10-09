using System.Globalization;
using System.Security.Claims;

namespace MockClient;

public sealed record GroupLevel(string Group, int Level);

/// <summary>What GET /api/userinfo returns: the claims of the gateway token, in a friendly shape.</summary>
public sealed record UserInfo(
    string? Sub,
    string? Email,
    string? TenantApp,
    IReadOnlyList<string> Groups,
    IReadOnlyList<int> Levels,
    IReadOnlyList<GroupLevel> GroupLevels,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>
    /// The token's "groups" and "levels" claims line up: levels[i] is the level of groups[i].
    /// An empty array means the user holds no groups in this app.
    /// </summary>
    public static UserInfo From(ClaimsPrincipal user)
    {
        var groups = user.FindAll("groups").Select(c => c.Value).ToList();

        var levels = new List<int>();
        foreach (var claim in user.FindAll("levels"))
        {
            if (int.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
            {
                levels.Add(level);
            }
        }

        var pairs = groups.Zip(levels, (group, level) => new GroupLevel(group, level)).ToList();

        DateTimeOffset? expiresAt = null;
        if (long.TryParse(user.FindFirst("exp")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp);
        }

        return new UserInfo(
            user.FindFirst("sub")?.Value,
            user.FindFirst("email")?.Value,
            user.FindFirst("tenant_app")?.Value,
            groups,
            levels,
            pairs,
            expiresAt);
    }
}
