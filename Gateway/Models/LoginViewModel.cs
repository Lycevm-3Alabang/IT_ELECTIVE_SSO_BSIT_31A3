using System.ComponentModel.DataAnnotations;

namespace Gateway.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Where to send the user afterwards. Re-checked on every POST, never trusted.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Display only. Filled from the app registry when the form is shown.</summary>
    public string? AppName { get; set; }
}
