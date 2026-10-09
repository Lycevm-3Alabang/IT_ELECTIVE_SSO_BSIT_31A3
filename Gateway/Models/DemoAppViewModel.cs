namespace Gateway.Models;

/// <summary>What the built-in demo app shows: a greeting, and who signed in if a token came back.</summary>
public class DemoAppViewModel
{
    public string AppName { get; set; } = "Sample App";

    public string? Email { get; set; }

    public List<string> Groups { get; set; } = new();

    public string? Error { get; set; }

    /// <summary>Where the "Sign in with SSO" button sends the browser.</summary>
    public string SignInUrl { get; set; } = string.Empty;

    public bool IsSignedIn => !string.IsNullOrEmpty(Email);
}
