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

(`Tests/Gateway.Tests`, `Tests/Data.Tests`.)
