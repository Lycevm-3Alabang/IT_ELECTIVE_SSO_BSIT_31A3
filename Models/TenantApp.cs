namespace Models;

/// <summary>
/// A client app registered with the gateway. Only enabled apps may receive a login token,
/// and only at their registered ReturnUrl.
/// </summary>
public class TenantApp
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Group> Groups { get; set; } = new List<Group>();
}
