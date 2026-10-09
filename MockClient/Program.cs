using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using MockClient;

// A minimal client app for the SSO gateway:
//   GET /              home page with the "Login with SSO" button
//   GET /login         redirects to the gateway login, asking it to return to /callback
//   GET /callback      the gateway sends the user back here with ?token=...
//   GET /api/userinfo  needs "Authorization: Bearer <token>", returns email, groups and levels
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configured lazily so the values are read after all configuration sources are in place.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, config) =>
    {
        // Keep claim names exactly as the gateway wrote them ("sub", "email", "groups", ...).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = TokenRules.Create(config);
        options.Events = new JwtBearerEvents
        {
            OnChallenge = ChallengeResponses.WriteChallengeAsync,
            OnForbidden = ChallengeResponses.WriteForbiddenAsync
        };
    });

builder.Services.AddAuthorization();
builder.Services
    .AddOptions<AuthorizationOptions>()
    .Configure<IConfiguration>((options, config) =>
        options.AddPolicy(TokenRules.AppPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim("tenant_app", TokenRules.AppName(config))));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Content(Pages.Home(), "text/html"));

app.MapGet("/login", (HttpContext http, IConfiguration config) =>
    Results.Redirect(SsoLinks.GatewayLoginUrl(http, config)));

app.MapGet("/callback", CallbackHandler.HandleAsync);

app.MapGet("/api/userinfo", (ClaimsPrincipal user) => Results.Ok(UserInfo.From(user)))
    .RequireAuthorization(TokenRules.AppPolicy);

app.Run();
