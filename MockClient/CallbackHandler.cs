using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MockClient;

/// <summary>
/// Where the gateway sends the browser after sign-in (GET /callback?token=...).
/// Checks the token itself and shows the result as a page.
/// </summary>
public static class CallbackHandler
{
    public static async Task HandleAsync(HttpContext http, IConfiguration config, string? token)
    {
        // The token sits in the URL, so keep this response out of caches and Referer headers.
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";

        if (string.IsNullOrWhiteSpace(token))
        {
            await WriteAsync(http, StatusCodes.Status400BadRequest,
                Pages.Problem("No sign-in token", "The gateway did not send a token. Start again from the home page."));
            return;
        }

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, TokenRules.Create(config));
        if (!result.IsValid)
        {
            var page = TokenRules.IsExpired(result.Exception)
                ? Pages.Problem("Your sign-in has expired", "The token is past its expiry time. Sign in again to get a new one.")
                : Pages.Problem("Sign-in token rejected", "The token could not be verified. Sign in again.");
            await WriteAsync(http, StatusCodes.Status401Unauthorized, page);
            return;
        }

        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        if (principal.FindFirst("tenant_app")?.Value != TokenRules.AppName(config))
        {
            await WriteAsync(http, StatusCodes.Status403Forbidden,
                Pages.Problem("Wrong app", "This token was issued for a different app."));
            return;
        }

        await WriteAsync(http, StatusCodes.Status200OK, Pages.SignedIn(UserInfo.From(principal), token));
    }

    private static async Task WriteAsync(HttpContext http, int status, string html)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(html);
    }
}
