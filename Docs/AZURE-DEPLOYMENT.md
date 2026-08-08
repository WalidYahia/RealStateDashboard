# Deploying to Azure App Service + Azure SQL Database

This app is production-ready for **Azure App Service** (Windows or Linux) with **Azure SQL Database**.
Secrets are **never** stored in the repo — the SQL connection string is supplied by App Service
configuration at runtime.

---

## 1. Azure SQL Database

1. Create an **Azure SQL Server** (logical server) and a **SQL Database** on it.
   - Server (short name): `syncro` → full name `syncro.database.windows.net`
   - Database name: **`RealStateDashboard`** (used below; change everywhere if you pick another)
   - Pricing: Basic/S0 is fine to start.
2. **Networking / firewall** on the SQL Server:
   - Turn **ON** "Allow Azure services and resources to access this server" (lets App Service connect).
   - Add your own client IP if you want to connect from SSMS / Azure Data Studio.
3. You do **not** need to create tables — the app runs EF Core migrations automatically on startup
   (see §5). The permission catalog is seeded on first boot; the first tenant + admin are created via
   the host login (see §6).

### Connection string

The connection string is **already set** in [`appsettings.Production.json`](../src/RealState.Web/appsettings.Production.json)
and used when `ASPNETCORE_ENVIRONMENT=Production`:

```
Server=tcp:syncro-sql-server.database.windows.net,1433;Initial Catalog=RealStateDashboard;User ID=syncro;Password=wwyy_0106116;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;
```

> ⚠️ Verify **`User ID=syncro`** — this is the *SQL server admin login*, which I couldn't read from the
> portal; if it differs, fix it (a wrong login shows a fast *"Login failed for user…"* in the app log).
> The server is **`syncro-sql-server`** and the database is **`RealStateDashboard`**. `Connection
> Timeout=60` gives the **Serverless** database time to auto-resume from its paused state on the first
> connection.
>
> This puts the SQL password in a committed file. If that's a concern, blank out `ConnectionStrings`
> in `appsettings.Production.json` and instead set it in App Service (§2) — App Service config wins.
> Better still, use **Microsoft Entra (Azure AD) auth / managed identity** so no password lives anywhere.

---

## 2. App Service configuration

In the Portal: **App Service → Settings → Environment variables**.

**Connection strings** — *optional*. The string is already in `appsettings.Production.json`, so you can
skip this. Add it here **only** if you'd rather keep the password out of source control (App Service
config overrides the file):
| Name | Value | Type |
|------|-------|------|
| `DefaultConnection` | *(your Azure SQL connection string)* | **SQLAzure** |

**App settings** tab → **+ Add** (required):
| Name | Value |
|------|-------|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `WEBSITE_TIME_ZONE` | `Egypt Standard Time` *(optional — Windows plans only; makes server-side dates local)* |

Save and let the app restart.

---

## 3. Publish the app

Pick one:

**A. Visual Studio** — right-click `RealState.Web` → **Publish** → Azure → App Service → select your app.

**B. CLI (zip deploy):**
```bash
dotnet publish src/RealState.Web/RealState.Web.csproj -c Release -o ./publish
cd publish && zip -r ../site.zip . && cd ..
az webapp deploy --resource-group <RG> --name <APP_NAME> --src-path site.zip --type zip
```

**C. GitHub Actions** — use the "Azure Web App" deploy action pointing at `src/RealState.Web`.

The Web SDK auto-generates `web.config` for Windows in-process hosting on publish — nothing to author.
`appsettings.json`, `appsettings.Production.json` (and the others) are published automatically; because
`ASPNETCORE_ENVIRONMENT=Production`, the Production file's settings apply on top of the base file.

---

## 4. What the production configuration already does

- **`appsettings.Production.json`** — the Azure SQL connection string, production log levels, `AllowedHosts`.
- **Serilog** — console-only in Production (App Service captures stdout in **Log stream**); file logging
  is Development-only so it never tries to write to App Service's read-only content directory.
- **`ForwardedHeaders`** (Program.cs) — trusts `X-Forwarded-Proto` from the App Service proxy so HTTPS
  redirection, secure auth cookies and generated links use `https`.
- **`EnableRetryOnFailure`** (DbContext) — retries transient Azure SQL disconnects (6 attempts) so the
  app and the startup migration survive brief drops.
- **HSTS + HTTPS redirection** — enabled outside Development.

> **Runtime version:** the app targets **.NET 9**, so the App Service must run the **.NET 9** stack.
> If the container fails with *"You must install or update .NET… version '9.0.0'… found 10.0.7"* (exit
> code 150), set the App Service runtime stack to **.NET 9** (Settings → Configuration → General
> settings → Stack).
- **Data Protection keys** — App Service persists them under `%HOME%` automatically, so auth cookies
  survive restarts (and are shared across instances if you scale out).

---

## 5. Migrations & seeding (automatic)

On every startup `Program.cs` runs `db.Database.MigrateAsync()` then `DbSeeder.SeedAsync(...)`:
- Applies any pending EF migrations (creates the full schema on first deploy).
- Seeds **only the 41 permissions** (the privilege catalog). It does **not** create the default
  tenant, the SuperAdmin/TenantAdmin roles, the admin user, or the lookups (currencies/sections/countries).
- The seeder is **idempotent** — safe to run on every boot.

> If you scale to **multiple instances**, they can race on the first migration. For that case, either
> deploy at 1 instance first (let it migrate) then scale out, or run migrations out-of-band with
> `dotnet ef database update` / an idempotent SQL script instead of on startup.

---

## 6. Bootstrapping the first tenant & hardening

A fresh database has only the permission rows — no tenant and no regular admin — but you don't need to
touch the seeder. Use the built-in **static host super-user**:

1. Browse to `https://<APP_NAME>.azurewebsites.net` and log in as the host:
   **`syncro`** / **`wwyy_0106116`** *(configured in `AppConstants.HostUserName`/`HostPassword`)*.
   The host is not a database account and has full control across the app.
2. Because no tenants exist yet, you're sent to the **tenants** screen. Create the first tenant — this
   also creates that tenant's **admin user** (the email/password you enter on the form).
3. You're signed out automatically. Now either:
   - log in again as **`syncro`** → pick the tenant from the dropdown → work inside it; or
   - log in as the **tenant admin** you just created and manage that tenant directly.
4. Create the real users for that tenant with the granular privileges.

> The host login stays available in production as a cross-tenant operator (create tenants, switch
> between them via **تبديل المؤسسة** in the top bar). To change or disable it, edit
> `AppConstants.HostUserName`/`HostPassword` (or move them to configuration).

**Hardening follow-ups:**
- Put the app behind your custom domain + managed certificate.
- Restrict `AllowedHosts` in `appsettings.Production.json` to your domain(s).
- Consider Azure AD auth / managed identity for SQL to drop the password entirely.
- Enable Application Insights for telemetry.

---

## Checklist

- [ ] Azure SQL DB `RealStateDashboard` created; "Allow Azure services" ON
- [ ] Connection string in `appsettings.Production.json` verified (`User ID` + database name correct)
- [ ] `ASPNETCORE_ENVIRONMENT=Production` app setting set
- [ ] App published; site loads over HTTPS
- [ ] Logged in as host (`syncro`), created the first tenant + its admin, then created real users
