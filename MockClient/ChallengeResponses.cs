using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace MockClient;

/// <summary>
/// JSON answers for API callers that are not signed in. An expired token gets its own
/// error code and the gateway login address, so a caller knows to sign in again
/// instead of guessing why it was refused.
/// </summary>
public static class ChallengeResponses
{
    public static async Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var (code, message) = context.AuthenticateFailure switch
        {
            null => ("missing_token", "Send the gateway token as 'Authorization: Bearer <token>'."),
            var failure when TokenRules.IsExpired(failure) =>
                ("token_expired", "The token has expired. Sign in again through the SSO gateway."),
            _ => ("invalid_token", "The token could not be verified. Sign in again through the SSO gateway.")
        };

        var http = context.HttpContext;
        var config = http.RequestServices.GetRequiredService<IConfiguration>();

        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.Headers.WWWAuthenticate = code == "missing_token"
            ? "Bearer"
            : $"Bearer error=\"invalid_token\", error_description=\"{message}\"";

        await http.Response.WriteAsJsonAsync(new
        {
            error = code,
            message,
            loginUrl = SsoLinks.GatewayLoginUrl(http, config)
        });
    }

    public static async Task WriteForbiddenAsync(ForbiddenContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "wrong_app",
            message = "This token was issued for a different app."
        });
    }
}
