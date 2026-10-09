using System.Net;
using System.Text.Json;

namespace MockClient;

/// <summary>The few HTML pages of the mock client, kept as strings so the project stays one small API.</summary>
public static class Pages
{
    private const string Style = """
        body { font-family: "Segoe UI", Arial, sans-serif; background: #f4f6fb; color: #1b2437; margin: 0;
               min-height: 100vh; display: grid; place-items: center; padding: 24px; }
        main { width: 100%; max-width: 560px; background: #fff; border-radius: 16px; padding: 32px;
               border-top: 8px solid #1d4ed8; box-shadow: 0 12px 32px rgba(18, 48, 143, .15); }
        h1 { margin-top: 0; color: #12308f; }
        .button { display: inline-block; padding: 10px 24px; border-radius: 999px; background: #1d4ed8; color: #fff;
                  text-decoration: none; font-weight: 700; border: 0; cursor: pointer; font-size: 15px; }
        .button:hover { background: #12308f; }
        .error { background: #fdecec; color: #a00000; padding: 12px; border-radius: 10px; }
        pre, textarea { width: 100%; box-sizing: border-box; background: #eef2fb; border-radius: 8px; padding: 10px;
                        font-size: 12px; overflow: auto; border: 0; }
        li { margin: 4px 0; }
        """;

    public static string Home() => Shell("Mock Client App", """
        <h1>Mock Client App</h1>
        <p>A tiny client app registered with the SSO gateway. It checks the JWT the gateway sends back.</p>
        <p><a class="button" href="/login">Login with SSO</a></p>
        <p>API: <code>GET /api/userinfo</code> with <code>Authorization: Bearer &lt;token&gt;</code></p>
        """);

    public static string Problem(string title, string message) => Shell(title, $"""
        <h1>{WebUtility.HtmlEncode(title)}</h1>
        <p class="error" role="alert">{WebUtility.HtmlEncode(message)}</p>
        <p><a class="button" href="/login">Login with SSO</a></p>
        """);

    public static string SignedIn(UserInfo info, string token)
    {
        var groups = info.GroupLevels.Count == 0
            ? "<li>No groups in this app</li>"
            : string.Join("", info.GroupLevels.Select(g =>
                $"<li>{WebUtility.HtmlEncode(g.Group)} (level {g.Level})</li>"));

        var expires = info.ExpiresAt?.ToString("u") ?? "unknown";

        return Shell("Signed in", $$"""
            <h1>Hello, {{WebUtility.HtmlEncode(info.Email ?? "unknown")}}</h1>
            <p>Signed in to <strong>{{WebUtility.HtmlEncode(info.TenantApp ?? "")}}</strong> through the SSO gateway.
               Token expires {{WebUtility.HtmlEncode(expires)}}.</p>
            <h3>Groups</h3>
            <ul>{{groups}}</ul>
            <h3>Token</h3>
            <textarea id="token" rows="5" readonly></textarea>
            <p><button class="button" id="call">Call GET /api/userinfo</button></p>
            <pre id="out"></pre>
            <script>
              const token = {{JsonSerializer.Serialize(token)}};
              document.getElementById('token').value = token;
              history.replaceState(null, '', '/callback');
              document.getElementById('call').addEventListener('click', async () => {
                const res = await fetch('/api/userinfo', { headers: { Authorization: 'Bearer ' + token } });
                document.getElementById('out').textContent = res.status + ' ' + JSON.stringify(await res.json(), null, 2);
              });
            </script>
            """);
    }

    private static string Shell(string title, string body) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>{{WebUtility.HtmlEncode(title)}}</title>
          <style>{{Style}}</style>
        </head>
        <body><main>{{body}}</main></body>
        </html>
        """;
}
