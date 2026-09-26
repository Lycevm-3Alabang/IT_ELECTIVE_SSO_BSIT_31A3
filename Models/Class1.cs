namespace Models;

public class TenantApp
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The URL this app is allowed to be redirected back to after a
    /// successful sign-in. Incoming returnUrl query values are matched
    /// against this so only registered apps can use the login page.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Apps can be registered but temporarily disabled without deleting
    /// them; disabled apps are treated the same as unregistered ones by
    /// the return URL validator.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public ICollection<Group> Groups { get; set; } = new List<Group>();
}