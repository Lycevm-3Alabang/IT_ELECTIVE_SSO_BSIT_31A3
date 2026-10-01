namespace Models;

public class Group
{
    public int Id { get; set; }

    public int TenantAppId { get; set; }

    public string Name { get; set; } = string.Empty;

    // 0 = highest power, higher numbers = less power.
    public int PowerLevel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TenantApp TenantApp { get; set; } = null!;
}
