namespace Models;

public class Group
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Access level this group grants inside its app. Goes into the JWT "levels" claim,
    /// in the same order as the "groups" claim.
    /// </summary>
    public int Level { get; set; }

    public int TenantAppId { get; set; }

    public TenantApp TenantApp { get; set; } = null!;
}