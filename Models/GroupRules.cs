namespace Models;

/// <summary>
/// Naming and power-level rules for <see cref="Group"/>. They live in one place
/// so the admin UI, the tests and any future API all agree on them.
///
/// Groups are stored as "[AppName]-[GroupName]" (for example "HR Portal-Admins")
/// and carry a <see cref="Group.PowerLevel"/> that external apps read to decide
/// what a member may do. 0 is the highest power; higher numbers mean less power.
/// </summary>
public static class GroupRules
{
    /// <summary>0 is the highest power level.</summary>
    public const int MinPowerLevel = 0;

    /// <summary>The lowest power an admin can assign. Raise this if more tiers are ever needed.</summary>
    public const int MaxPowerLevel = 100;

    /// <summary>
    /// Length of the stored "[AppName]-[GroupName]" value. Matches the
    /// Groups.Name column (HasMaxLength(100)) in SsoDbContext.
    /// </summary>
    public const int MaxNameLength = 100;

    public const char Separator = '-';

    public static bool IsValidPowerLevel(int level) =>
        level is >= MinPowerLevel and <= MaxPowerLevel;

    /// <summary>The "[AppName]-" prefix every group of that app starts with.</summary>
    public static string Prefix(string appName) => appName.Trim() + Separator;

    /// <summary>
    /// The group name without the app prefix. If the admin already typed the
    /// prefix ("HR Portal-Admins" for the HR Portal app) it is removed so the
    /// stored name never ends up double-prefixed.
    /// </summary>
    public static string ShortName(string appName, string? groupName)
    {
        var name = (groupName ?? string.Empty).Trim();
        var prefix = Prefix(appName);

        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[prefix.Length..].Trim();
        }

        return name;
    }

    /// <summary>Builds the stored name: "[AppName]-[GroupName]".</summary>
    public static string BuildName(string appName, string? groupName) =>
        Prefix(appName) + ShortName(appName, groupName);

    /// <summary>
    /// Swaps the app prefix on an already-stored group name after the app is
    /// renamed. Names that don't start with the old prefix are returned unchanged.
    /// </summary>
    public static string ReplacePrefix(string oldAppName, string newAppName, string storedName)
    {
        var oldPrefix = Prefix(oldAppName);

        return storedName.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)
            ? Prefix(newAppName) + storedName[oldPrefix.Length..]
            : storedName;
    }
}
