namespace Models;

public class Group
{
    public int Id { get; set; }

    /// <summary>Stored as "[AppName]-[GroupName]".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Access level this group grants inside its app (0 = highest power). Goes into the JWT
    /// "levels" claim, in the same order as the "groups" claim.
    /// </summary>
    public int PowerLevel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int TenantAppId { get; set; }

    public TenantApp TenantApp { get; set; } = null!;
}
