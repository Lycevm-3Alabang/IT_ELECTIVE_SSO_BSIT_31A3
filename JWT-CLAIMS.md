# JWT claims issued by the SSO gateway

After a successful sign-in at `/Auth/Login`, the gateway redirects the browser to the app's registered
return URL with the token in the query string: `https://your-app/callback?token=<jwt>`.

The token is a signed JWT (HS256). An app is expected to verify the signature, issuer, audience and
expiry, and to check that `tenant_app` is its own name.

## Claims

| Claim | Type | Meaning |
| --- | --- | --- |
| `sub` | string | The user's id in the gateway (stable, use this as the key for the user). |
| `email` | string | The user's email address. |
| `tenant_app` | string | Name of the app the token was issued for (as registered under Admin > Apps). |
| `groups` | array of strings | The user's groups **in this app only**, stored as `[AppName]-[GroupName]`, sorted by name. `[]` when the user has none. |
| `levels` | array of integers | Power level of each group, **same order as `groups`**: `levels[i]` belongs to `groups[i]`. `0` is the highest power; higher numbers mean less power. |
| `iss` | string | Issuer, `JwtSettings:Issuer` (default `SSOGateway`). |
| `aud` | string | Audience, `JwtSettings:Audience` (default `SSOClientApps`). |
| `iat` | number | Issued at (Unix seconds). |
| `exp` | number | Expires at (Unix seconds). Lifetime is `JwtSettings:ExpiryHours` (default 9 hours). |

Every app shares the same issuer, audience and key, so a token for one app also verifies in another.
**Check `tenant_app`** (the mock client does this) so an app only accepts tokens issued for itself.

## Example payload

```json
{
  "sub": "5b0f6c1e-0d4c-4a1b-9d66-2a3f1c7e9a10",
  "email": "flow.user@example.test",
  "tenant_app": "Mock Client",
  "groups": ["Mock Client-Editors", "Mock Client-Viewers"],
  "levels": [2, 5],
  "iss": "SSOGateway",
  "aud": "SSOClientApps",
  "iat": 1790000000,
  "exp": 1790003600
}
```

Here the user is an Editor (level 2) and a Viewer (level 5) in "Mock Client".

## Reading the claims in .NET

`groups` and `levels` are JSON arrays, so a validated principal has one `groups` claim and one `levels`
claim per entry. Turn off claim-name mapping (`MapInboundClaims = false`) to keep the names above:

```csharp
var groups = user.FindAll("groups").Select(c => c.Value).ToList();
var levels = user.FindAll("levels").Select(c => int.Parse(c.Value)).ToList();
```

## Expiry

When `exp` has passed, validation fails with an "expired" error. The right response is to send the user
back to `/Auth/Login?returnUrl=<your callback>` for a new token; the gateway does not refresh tokens.
