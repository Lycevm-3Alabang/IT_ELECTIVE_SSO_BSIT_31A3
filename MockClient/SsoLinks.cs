namespace MockClient;

/// <summary>Builds the addresses used to send a user to the gateway and back.</summary>
public static class SsoLinks
{
    private const string DefaultGatewayBaseUrl = "https://localhost:7096";

    /// <summary>
    /// The address the gateway sends the user back to. This exact address must be registered
    /// in the gateway under Admin &gt; Apps. Set MockClient:CallbackUrl to override it
    /// (for example behind a proxy).
    /// </summary>
    public static string CallbackUrl(HttpContext http, IConfiguration config) =>
        config["MockClient:CallbackUrl"] is { Length: > 0 } configured
            ? configured
            : $"{http.Request.Scheme}://{http.Request.Host}/callback";

    public static string GatewayLoginUrl(HttpContext http, IConfiguration config)
    {
        var gateway = (config["Gateway:BaseUrl"] is { Length: > 0 } url ? url : DefaultGatewayBaseUrl).TrimEnd('/');
        return $"{gateway}/Auth/Login?returnUrl={Uri.EscapeDataString(CallbackUrl(http, config))}";
    }
}
