using System.Collections.Generic;

namespace Models;

public class TenantApp
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }

    // Navigation property for groups (kept minimal)
    public ICollection<Group> Groups { get; set; } = new List<Group>();
}
