using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Models;

namespace Gateway.Services;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public double ExpiryHours { get; set; } = 9;
}

public interface IJwtTokenService
{
    /// <summary>
    /// Builds a signed token for one user and one client app.
    /// <paramref name="levels"/> lines up with <paramref name="groups"/>: levels[i] is the level of groups[i].
    /// </summary>
    string CreateToken(ApplicationUser user, string tenantApp, IReadOnlyList<string> groups, IReadOnlyList<int> levels);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private const int MinimumKeyBytes = 32; // HS256 wants at least 256 bits

    private readonly JwtSettings _settings;
    private readonly TimeProvider _time;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtSettings> options, TimeProvider time)
    {
        _settings = options.Value;
        _time = time;

        var keyBytes = Encoding.UTF8.GetBytes(_settings.SecretKey ?? string.Empty);
        if (keyBytes.Length < MinimumKeyBytes)
        {
            throw new InvalidOperationException(
                $"JwtSettings:SecretKey must be at least {MinimumKeyBytes} bytes long.");
        }

        _credentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    }

    public string CreateToken(ApplicationUser user, string tenantApp, IReadOnlyList<string> groups, IReadOnlyList<int> levels)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = now,
            Expires = now.AddHours(_settings.ExpiryHours),
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id,
                ["email"] = user.Email ?? string.Empty,
                ["tenant_app"] = tenantApp,
                ["groups"] = groups.ToArray(),
                ["levels"] = levels.ToArray()
            }
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
