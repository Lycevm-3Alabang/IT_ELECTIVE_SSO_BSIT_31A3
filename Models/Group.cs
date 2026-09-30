namespace Models;

public class Group
{
    public int Id { get; set; }

    /// <summary>
    /// Fully-qualified group name, auto-prefixed with the owning app's name
    /// on create (e.g. "SalesApp-Admin"). This is the value that ends up in
    /// the JWT "groups" claim, so it must stay unique per app.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public int TenantAppId { get; set; }

    public TenantApp TenantApp { get; set; } = null!;

    /// <summary>
    /// Access level for this group. 0 = highest power, higher numbers mean
    /// less power. Included in the JWT "levels" claim so client apps can
    /// tell how much access a member of this group has.
    /// </summary>
    public int PowerLevel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}