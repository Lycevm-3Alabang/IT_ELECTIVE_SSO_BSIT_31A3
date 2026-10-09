using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// End to end: mock client -> gateway login -> back to the mock client with a JWT -> /api/userinfo.
/// Both apps run for real (in memory); only the browser is played by HttpClient.
/// </summary>
public class FullSsoFlowTests(SsoFlowFixture sso) : IClassFixture<SsoFlowFixture>
{
    [Fact]
    public async Task LoginWithSso_RedirectsToGatewayLogin_AskingToComeBackToTheCallback()
    {
        using var mock = sso.NewMockClient();

        var response = await mock.GetAsync("/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(SsoFlowFixture.GatewayHost + "/Auth/Login", location.ToString());
        Assert.Equal(SsoFlowFixture.CallbackUrl, QueryHelpers.ParseQuery(location.Query)["returnUrl"].ToString());
    }

    [Fact]
    public async Task FullFlow_SignInAtGateway_ThenUserInfoReturnsEmailGroupsAndLevels()
    {
        using var mock = sso.NewMockClient();
        using var gateway = sso.NewGatewayClient();

        var token = await SignInAsync(mock, gateway);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await mock.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(SsoFlowFixture.UserEmail, info.GetProperty("email").GetString());
        Assert.Equal(SsoFlowFixture.AppName, info.GetProperty("tenantApp").GetString());

        // Only this app's groups are in the token; the group of "Other App" is not.
        Assert.Equal(
            SsoFlowFixture.ExpectedGroups,
            info.GetProperty("groups").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(
            SsoFlowFixture.ExpectedLevels,
            info.GetProperty("levels").EnumerateArray().Select(e => e.GetInt32()).ToArray());

        var pairs = info.GetProperty("groupLevels").EnumerateArray().ToArray();
        Assert.Equal("Mock Client-Editors", pairs[0].GetProperty("group").GetString());
        Assert.Equal(2, pairs[0].GetProperty("level").GetInt32());
        Assert.Equal("Mock Client-Viewers", pairs[1].GetProperty("group").GetString());
        Assert.Equal(5, pairs[1].GetProperty("level").GetInt32());
    }

    [Fact]
    public async Task Callback_WithTokenFromGateway_ShowsWhoSignedIn()
    {
        using var mock = sso.NewMockClient();
        using var gateway = sso.NewGatewayClient();

        var token = await SignInAsync(mock, gateway);
        var page = await mock.GetAsync("/callback?token=" + Uri.EscapeDataString(token));

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains(SsoFlowFixture.UserEmail, html);
        Assert.Contains("Mock Client-Editors", html);
    }

    [Fact]
    public async Task UserInfo_WithoutToken_IsRejected_WithMissingTokenAndLoginUrl()
    {
        using var mock = sso.NewMockClient();

        var response = await mock.GetAsync("/api/userinfo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("missing_token", body.GetProperty("error").GetString());
        Assert.StartsWith(SsoFlowFixture.GatewayHost + "/Auth/Login", body.GetProperty("loginUrl").GetString());
    }

    [Fact]
    public async Task UserInfo_WithExpiredToken_SaysTokenExpired_AndPointsBackToTheGateway()
    {
        using var mock = sso.NewMockClient();
        var expired = SsoFlowFixture.MintToken(expiresIn: TimeSpan.FromHours(-1));

        var response = await GetUserInfoAsync(mock, expired);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("token_expired", body.GetProperty("error").GetString());
        Assert.StartsWith(SsoFlowFixture.GatewayHost + "/Auth/Login", body.GetProperty("loginUrl").GetString());
    }

    [Fact]
    public async Task Callback_WithExpiredToken_ShowsExpiredPage_WithSignInAgain()
    {
        using var mock = sso.NewMockClient();
        var expired = SsoFlowFixture.MintToken(expiresIn: TimeSpan.FromHours(-1));

        var page = await mock.GetAsync("/callback?token=" + Uri.EscapeDataString(expired));

        Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("expired", html);
        Assert.Contains("/login", html);
    }

    [Fact]
    public async Task UserInfo_WithTokenSignedByAnotherKey_IsRejectedAsInvalid()
    {
        using var mock = sso.NewMockClient();
        var forged = SsoFlowFixture.MintToken(key: "a-completely-different-key-with-32-plus-bytes");

        var response = await GetUserInfoAsync(mock, forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_token", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task UserInfo_WithTokenIssuedForAnotherApp_IsForbidden()
    {
        using var mock = sso.NewMockClient();
        var otherApp = SsoFlowFixture.MintToken(tenantApp: "Other App");

        var response = await GetUserInfoAsync(mock, otherApp);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Plays the browser: click "Login with SSO", fill in the gateway form, land on the callback.</summary>
    private static async Task<string> SignInAsync(HttpClient mock, HttpClient gateway)
    {
        // 1. "Login with SSO" on the mock client sends the browser to the gateway.
        var start = await mock.GetAsync("/login");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var loginUrl = start.Headers.Location!;
        var returnUrl = QueryHelpers.ParseQuery(loginUrl.Query)["returnUrl"].ToString();

        // 2. The gateway shows its login form (and sets the antiforgery cookie).
        var form = await gateway.GetAsync(loginUrl.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var html = await form.Content.ReadAsStringAsync();
        var antiforgery = ExtractAntiforgeryToken(html);

        // 3. The user submits email and password.
        var post = await gateway.PostAsync("/Auth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = SsoFlowFixture.UserEmail,
            ["Password"] = SsoFlowFixture.UserPassword,
            ["ReturnUrl"] = returnUrl,
            ["__RequestVerificationToken"] = antiforgery
        }));

        // 4. The gateway sends the browser back to the callback with the token.
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var back = post.Headers.Location!;
        Assert.Equal(SsoFlowFixture.CallbackUrl, back.GetLeftPart(UriPartial.Path));
        var token = QueryHelpers.ParseQuery(back.Query)["token"].ToString();
        Assert.False(string.IsNullOrEmpty(token));
        return token;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var tag = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*>");
        Assert.True(tag.Success, "The gateway login form has no antiforgery field.");

        var value = Regex.Match(tag.Value, "value=\"([^\"]+)\"");
        Assert.True(value.Success, "The antiforgery field has no value.");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> GetUserInfoAsync(HttpClient mock, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await mock.SendAsync(request);
    }
}
