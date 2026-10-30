# Deploying TaskDesk to IIS (Windows Server)

## 1. One-time server preparation
1. **Windows Server 2019/2022** with the **Web Server (IIS)** role.
2. Install the **ASP.NET Core 10 Hosting Bundle** (runtime + ASP.NET Core Module V2). Then restart IIS: `iisreset`.
   Verify: IIS Manager → Modules contains `AspNetCoreModuleV2`.
3. Folders: `D:\Sites\TaskDesk` (site), `D:\Backups\TaskDesk` (release backups).
4. Request a TLS certificate from the internal CA for `taskdesk.office.local` and import it into *Local Computer → Personal*.

## 2. Database
1. Create database `TaskDeskDb` (SQL Server 2019+), recovery model **FULL**.
2. Create a login for the app pool identity and grant least privilege:
   ```sql
   CREATE LOGIN [OFFICE\WEB01$] FROM WINDOWS;          -- machine account when using ApplicationPoolIdentity
   USE TaskDeskDb;
   CREATE USER [OFFICE\WEB01$] FOR LOGIN [OFFICE\WEB01$];
   ALTER ROLE db_datareader ADD MEMBER [OFFICE\WEB01$];
   ALTER ROLE db_datawriter ADD MEMBER [OFFICE\WEB01$];
   GRANT EXECUTE ON SCHEMA::dbo TO [OFFICE\WEB01$];
   ```
3. Schema and stored procedures: the first deployment can create them automatically
   (`Database:InitializeOnStartup=true`, needs `db_ddladmin` once), **or** a DBA applies a migration script:
   ```bash
   dotnet ef migrations add InitialCreate --project src/TaskDesk.Web --output-dir Data/Migrations
   dotnet ef migrations script --idempotent --project src/TaskDesk.Web -o TaskDesk-schema.sql
   ```
   plus `database/01_Reporting.sql` and `database/02_Maintenance.sql`. Afterwards set
   `Database:InitializeOnStartup=false` in production.

## 3. Configuration (secrets stay on the server)
Create `D:\Sites\TaskDesk\appsettings.Production.json` (preserved by the deploy script, restrict NTFS
read access to Administrators and the app pool identity):
```json
{
  "ConnectionStrings": {
    "TaskDesk": "Server=SQL01;Database=TaskDeskDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=False"
  },
  "Database": { "InitializeOnStartup": false },
  "Integration": {
    "HolidayService": { "Url": "https://hrservices.office.local/HolidayService.asmx" },
    "Clients": [ { "Name": "HelpdeskSystem", "ApiKeySha256": "<sha256 hex of the key>", "Enabled": true } ]
  }
}
```
Generate a key and its hash (PowerShell):
```powershell
$key  = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($key)))
"Give to Helpdesk team (securely): $key"; "Put in config: $hash"
```

## 4. Deploy a release
1. Download the `taskdesk-iis-package` artifact from the GitHub Actions run (or `dotnet publish -c Release -o publish` and zip).
2. On the web server (elevated PowerShell, inside the approved change window):
   ```powershell
   .\Deploy-TaskDesk.ps1 -PackagePath C:\Drops\taskdesk-1.0.0.zip -HealthUrl https://taskdesk.office.local/health/ready
   ```
   The script creates the app pool (*No Managed Code*, *AlwaysRunning*) and site if missing, backs up the
   current release, uses `app_offline.htm`, copies the new files, runs the health check and **rolls back
   automatically** if it fails.
3. First deployment only: IIS Manager → site → Bindings → add **https :443** with the certificate, remove http or
   keep it for redirect only.

## 5. Post-deployment checks
- `https://taskdesk.office.local/health/ready` → `{"status":"Healthy", ...}`
- Sign in, open the dashboard and a task.
- Response headers include `Content-Security-Policy`, `Strict-Transport-Security`, and no `Server` / `X-Powered-By`.
- Event Viewer → Application: no errors from *IIS AspNetCore Module V2* or *.NET Runtime*.

## 6. Rollback
Automatic on failed health check. Manual: run the script again with the previous package, or mirror
`D:\Backups\TaskDesk\release-<timestamp>` back into the site folder with `app_offline.htm` in place.
