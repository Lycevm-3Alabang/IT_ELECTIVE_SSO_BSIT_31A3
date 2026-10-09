# IT ELECTIVE - SSO Gateway (merged front end + back end)

ASP.NET Core (net10.0) single sign-on gateway. Client apps send users to `/Auth/Login?returnUrl=...`;
after sign-in the user is redirected back with a signed JWT in `?token=`. Admins manage users, apps and
groups from `/Admin`.

## Run it

Needs the .NET 10 SDK.

    cd Gateway
    dotnet run

Then open http://localhost:5111 (or the https address `dotnet run` prints).

The SQLite database (`Data/ssodatabase.db`) is created on first start, together with:

* an admin account: `admin@itelective-sso.local` / `ChangeMe!123` (change it in `Gateway/appsettings.json`
  under `AdminSeed` **before the first run**, and change the JWT `SecretKey` for anything real)
* one sample app, "Sample App" -> `https://example.com/callback`

If you ran an older build that already created `Data/ssodatabase.db`, delete that file once so the new
schema (app return URLs, group power levels) is created.

## Where things are

| Page | URL |
| --- | --- |
| Admin sign in | `/Account/Login` |
| Dashboard | `/Admin` |
| Users | `/Admin/Users` |
| Apps (register / edit / enable / delete) | `/Admin/TenantApps` |
| Groups | `/Admin/Groups` |
| Audit logs | `/Admin/AuditLogs` |
| Client sign-in (what apps redirect to) | `/Auth/Login?returnUrl=<registered url>` |

Try the client flow: sign in as admin, register an app under **External Apps** (for example
`https://localhost:9999/callback`), then open `/Auth/Login?returnUrl=https://localhost:9999/callback`.

## Tests

    dotnet test

(`Tests/Gateway.Tests`, `Tests/Data.Tests`, `Tests/Integration.Tests`.)

## Mock client app and the full SSO flow (Issue 18)

`MockClient/` is a small ASP.NET Core Web API that plays an app using the gateway. It validates the gateway's
JWT and has `GET /api/userinfo`, which returns the user's email, groups and levels. The claims are
documented in [`docs/JWT-CLAIMS.md`](docs/JWT-CLAIMS.md).

| Page / endpoint | What it does |
| --- | --- |
| `/` | Home page with the **Login with SSO** button |
| `/login` | Redirects to the gateway: `/Auth/Login?returnUrl=<MockClient>/callback` |
| `/callback` | The gateway sends the user back here with `?token=`; the token is checked and the result shown |
| `/api/userinfo` | Needs `Authorization: Bearer <token>`. Returns `email`, `groups`, `levels` (and `groupLevels`, `tenantApp`, `expiresAt`) |

### Run both apps together

1. Start the gateway (terminal 1): `cd Gateway && dotnet run --launch-profile https` (https://localhost:7096).
2. Start the mock client (terminal 2): `cd MockClient && dotnet run --launch-profile https` (https://localhost:7200).
3. In the gateway, sign in as the admin (`/Account/Login`) and open **Admin > Apps**. Register an app named exactly
   `Mock Client` with return URL `https://localhost:7200/callback`.
4. Under **Admin > Groups**, add a group to that app (for example `Editors`, level 2) and put a user in it.
5. Open https://localhost:7200 and press **Login with SSO**. Sign in as that user. You land on `/callback`,
   which shows the email and groups; the **Call GET /api/userinfo** button calls the API with the token.

The two apps must share the same `JwtSettings` (`Issuer`, `Audience`, `SecretKey`). Both ship with the same defaults
in their `appsettings.json`; if you change the gateway's key, change `MockClient/appsettings.json` too.
Other settings: `Gateway:BaseUrl` (where the gateway runs), `MockClient:AppName` (must match the registered app name)
and optionally `MockClient:CallbackUrl` if the callback address is not the one the app is served on.

### Expired or invalid tokens

* `/api/userinfo` with an expired token answers `401` with `{"error":"token_expired","message":...,"loginUrl":...}`;
  `loginUrl` is where to send the user to sign in again. A bad signature gives `invalid_token`, no token gives
  `missing_token`, and a token issued for another app gives `403`.
* `/callback` with an expired token shows a "Your sign-in has expired" page with a **Login with SSO** button.

### Integration test

`dotnet test Tests/Integration.Tests` starts the real gateway (on a temporary SQLite file) and the mock client in
memory, then signs a seeded user in through the gateway form, follows the redirect back with the token and calls
`/api/userinfo`. It also covers the expired, forged, missing and wrong-app token cases.

