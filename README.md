# YAF.NET Forums for Umbraco

[![NuGet](https://img.shields.io/nuget/v/YAFNET.Umbraco.Forums.svg)](https://nuget.org/packages/YAFNET.Umbraco.Forums)

Runs the [YAF.NET](https://yetanotherforum.net) forum inside an Umbraco site at `/Forums`, with
**single sign-on through Umbraco Members**: members log in once on the site and are logged in to the forum.

| | Version |
|---|---|
| Umbraco | 19 (net11) — currently built against `19.0.0-rc1` |
| YAF.NET | 4.0.11 (`YAFNET.RazorPages` + a data provider package) |

## How it works

- YAF's Razor Class Library (`YAFNET.RazorPages`, area `Forums`) serves the forum pages under `/Forums`.
- **Login:** the Umbraco member cookie is the only login. YAF finds the current user by
  `User.Identity.Name`, so a logged-in member *is* the forum user with the same username.
  - YAF's sign-in/sign-out (`SignInManager<AspNetUsers>`) is replaced by `MemberBridgeSignInManager`,
    which signs the matching **member** in/out. Logging out of the forum logs out of the site.
  - The first time a member opens the forum, the forum user is created automatically (`AutoProvisionMembers`).
  - YAF's login, register and password pages redirect to the Umbraco member pages (`YafForums:*Url`).
- **Isolation:** YAF's middlewares (AntiXss, security headers, session, install check, ban checks,
  rate limiter, localization, ...) only run for forum requests — matched by endpoint (any YAF controller,
  page or hub) — never for Umbraco content, backoffice or API requests. Registered through an
  `UmbracoPipelineFilter`, no changes to `UseUmbraco()` needed.
- **Backoffice:** a "Forums" dashboard in the Content section links to the forum and its administration.

## Installation

1. Add the packages (plus the YAF data provider you use):

   ```cmd
   dotnet add package YAFNET.Umbraco.Forums
   dotnet add package YAFNET.Data.SqlServer   # or .Sqlite / .MySql / .PostgreSQL
   ```

2. `Program.cs` — one line before `builder.Build()`:

   ```csharp
   builder.CreateUmbracoBuilder()
       .AddBackOffice()
       .AddWebsite()
       .AddComposers()
       .Build();

   builder.AddYafForums();   // YAF.NET under /Forums (switches the service provider to Autofac)

   WebApplication app = builder.Build();
   ```

   > YAF requires **Autofac** as the service provider; `AddYafForums()` sets it up for the whole host.

3. `appsettings.json` — the YAF connection string and settings, and the plugin options:

   ```jsonc
   "ConnectionStrings": {
     "DefaultConnection": "Server=...;Database=...;"   // the YAF database (can be the Umbraco database)
   },
   "YafForums": {
     "LoginUrl": "/login",          // the Umbraco member login page, empty = built-in page /forums-login
     "RegisterUrl": "/register",    // empty = LoginUrl
     "ForgotPasswordUrl": "",       // empty = LoginUrl
     "AutoProvisionMembers": true,
     "EnableBuiltInLoginPage": true
   },
   "BoardConfiguration": {
     "BoardID": 1,
     "ConnectionProviderName": "System.Data.SqlClient",
     "DatabaseObjectQualifier": "yaf_",   // prefix of the YAF tables, so they can live in the Umbraco database
     "DatabaseOwner": "dbo",
     "UseRateLimiter": true,
     "RateLimiterPermitLimit": 10
     // ... see https://github.com/YAFNET/YAFNET/wiki/BoardConfiguration-Settings
   },
   "MailConfiguration": { "Host": "localhost", "Port": 25 },
   "VapidConfiguration": { "PublicKey": "", "PrivateKey": "" }
   ```

   `BoardConfiguration:Area` is always set to `Forums` by the plugin.

4. Open `/Forums` and run the YAF install wizard. **The superuser you create there becomes a forum admin
   when an Umbraco member with the same username logs in** — create that member in the backoffice.

5. Link the forum from your site: `<a href="/Forums">Forum</a>`.

### The built-in login page

If `YafForums:LoginUrl` is empty, `/forums-login` is a minimal member login page, so the forum works right away.
For a real site, point `LoginUrl` (and `RegisterUrl`) to your Umbraco member pages — they must redirect
to the `returnUrl` query parameter after login. Set `EnableBuiltInLoginPage` to `false` to turn it off.

## Sample site

`samples/YAF.Umbraco.Forums.TestSite` is an Umbraco 19 site with SQLite for Umbraco and YAF:

```cmd
cd samples/YAF.Umbraco.Forums.TestSite
dotnet run
```

Umbraco installs unattended, and the sample members `forumadmin` and `alice` are created at startup.
**All sample credentials (backoffice admin, members) are in `appsettings.Development.json` — local test values only.**
Run the YAF install wizard at `/Forums` and use `forumadmin` as superuser name so that member is the forum admin.

## Known limitations

- **Umbraco's own email sending (Umbraco 19 RC1).** YAF 4.0.11 uses MailKit 5 (`MimeKit.Core`), Umbraco 19 RC1
  is compiled against MailKit 4 (`MimeKit`). Only one MailKit can be loaded, the package resolution picks MailKit 5,
  so Umbraco's `EmailSender` (password reset, invites, notifications) will fail when it sends.
  The forum's own emails work. Fixed once Umbraco and YAF use the same MailKit major version.
- YAF's static files are served from the site root (`/css`, `/js`, `/assets`, `/languages`, ...) and its API
  controllers use root routes (`/api/...`, `/Posts`, `/Poll`, ...). Avoid site files/routes with the same names.
- YAF's account features that work on YAF passwords (change password, two-factor, external logins in YAF) do not
  apply — accounts are managed by Umbraco Members. Changing a member's **username** creates a new forum user.
- Password rules (`IdentityOptions`) are shared with Umbraco Members; YAF's password settings are not applied.
- The forum pages use YAF's own layout. To use the site's header/footer, override
  `Areas/Forums/Pages/Shared/_Layout.cshtml` in the site (Razor Class Library views can be overridden).
- YAF's language switcher lists the site's request cultures, not YAF's languages (YAF's cultures are only applied
  inside the forum, so they don't change Umbraco's culture handling).

## Workarounds for YAF 4.0.11 (candidates for upstream fixes)

- `YAFNET.Core` contains `WebEssentials.AspNetCore.Pwa.dll`, which needs `BouncyCastle.Cryptography`, but the
  package does not declare the dependency — this plugin references it.
- In area mode the RCL ships images below `/Forums/images`, but YAF links `/images/...` (e.g. the board logo) —
  missing `/images/*` files are served from `/Forums/images`.
- The PWA `BaseRoute` is the service worker *scope* and the *prefix* of its routes. The plugin uses `/Forums/`
  (so the service worker does not control the whole Umbraco site) and maps `/Forums/serviceworker`,
  `/Forums/manifest.webmanifest`, `/Forums/offline.html` and `/Forums/assets/*` to YAF's root routes.
- `LoadPageFromDatabase` shows an identity without a forum user as guest (`GetPageLoad` falls back to the guest
  user), so its "create the forum user" branch is never reached — the plugin creates the forum user itself
  (like `ImportForumUser`).
- `AddYafCore`/`UseYafCore` can't be used inside another app with its own identity and pipeline. Possible hooks:
  an `AddYafCore(..., useHostIdentity: true)` option, and an area-scoped `UseYafCore`.
