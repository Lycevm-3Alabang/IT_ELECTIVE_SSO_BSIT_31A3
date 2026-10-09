using System.Text;
using Gateway.Models;
using Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Controllers;

/// <summary>
/// A tiny client app hosted by the gateway so there is always something to open from Admin > Apps.
/// The seeded "Sample App" is registered with this page's address (/DemoApp).
///
/// Opened directly it prints a greeting. Press "Sign in with SSO" and it goes through the normal
/// gateway login; the gateway sends the browser back here with ?token=..., which this page verifies
/// and greets the user by email.
/// </summary>
[AllowAnonymous]
[Route("DemoApp")]
public class DemoAppController : Controller
{
    private readonly JwtSettings _jwt;

    public DemoAppController(IOptions<JwtSettings> jwt)
    {
        _jwt = jwt.Value;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? token = null)
    {
        var model = new DemoAppViewModel
        {
            SignInUrl = Url.Action("Login", "Auth", new { returnUrl = AbsoluteSelfUrl() }) ?? "/Auth/Login"
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            await ReadTokenAsync(token, model);
        }

        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View(model);
    }

    private string AbsoluteSelfUrl() =>
        $"{Request.Scheme}://{Request.Host}{Url.Action("Index", "DemoApp")}";

    private async Task ReadTokenAsync(string token, DemoAppViewModel model)
    {
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = _jwt.Issuer,
            ValidAudience = _jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SecretKey)),
            ValidateLifetime = true
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        if (!result.IsValid)
        {
            model.Error = "The sign-in token could not be verified. Please try signing in again.";
            return;
        }

        if (result.Claims.TryGetValue("tenant_app", out var app) && app is not null)
        {
            model.AppName = app.ToString() ?? model.AppName;
        }

        model.Email = result.Claims.TryGetValue("email", out var email) ? email?.ToString() : null;

        if (result.Claims.TryGetValue("groups", out var groups) && groups is System.Collections.IEnumerable list && groups is not string)
        {
            foreach (var item in list)
            {
                if (item is not null)
                {
                    model.Groups.Add(item.ToString()!);
                }
            }
        }
    }
}
