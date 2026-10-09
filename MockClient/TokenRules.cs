using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MockClient;

/// <summary>
/// How this client decides whether a gateway token is acceptable. Everything is read from
/// configuration when it is needed, so the values just have to match the gateway's JwtSettings.
/// </summary>
public static class TokenRules
{
    public const string AppPolicy = "MockClientApp";
    public const string DefaultAppName = "Mock Client";
    private const int MinimumKeyBytes = 32;

    public static TokenValidationParameters Create(IConfiguration config)
    {
        var key = config["JwtSettings:SecretKey"] ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(key) < MinimumKeyBytes)
        {
            throw new InvalidOperationException(
                "JwtSettings:SecretKey must be at least 32 bytes long and identical to the gateway's key.");
        }

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config["JwtSettings:Issuer"],
            ValidateAudience = true,
            ValidAudience = config["JwtSettings:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Every app shares the gateway's issuer and audience, so the "tenant_app" claim is what says
    /// which app a token was issued for. This client only accepts tokens issued for its own name.
    /// </summary>
    public static string AppName(IConfiguration config) =>
        config["MockClient:AppName"] is { Length: > 0 } name ? name : DefaultAppName;

    public static bool IsExpired(Exception? failure) => failure switch
    {
        SecurityTokenExpiredException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsExpired),
        _ => false
    };
}
