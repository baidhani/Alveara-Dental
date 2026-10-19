# Running Alveara Dental on your own machine

How to start the whole application from a completely switched-off state with a brand-new empty database, and how to stop it, restart it, reset it and clean up after the tests. Written for the project owner. Every command here was run on the development machine (Windows 11, Windows PowerShell 5.1, SQL Server 2025 Express).

> **Dev-only logins.** The passwords below are for a local practice database that holds no real patients. Never reuse them anywhere else.

## What you are starting

| Piece | What it is | Where it runs | Needs its own window? |
|---|---|---|---|
| Database | SQL Server Express, instance `.\SQLEXPRESS`, a database named `AlveraDev` | a Windows service, starts by itself | no |
| API | The ASP.NET server (all the rules, security, data access) | `http://localhost:5072` | yes (window A) |
| Client | The React web app you see in the browser | `http://localhost:5173` (it forwards `/api` calls to the API) | yes (window B) |

You open the **client** in the browser. The browser never talks to the database; everything goes through the API.

## Where everything lives

Every database and every file the project creates sits under **one root**, `C:\.alvera-data`, and nowhere else:

```
C:\.alvera-data\
    Dev-Data\     what the dev app uses (AlveraDev and its files)
        Database    AlveraDev.mdf and AlveraDev_log.ldf
        Storage     uploaded documents
        Keys        the data-protection key ring
        Backup      backups, staging, restore and import folders
    Test-Data\    what the automated tests create (databases, backups, scratch)
        Database  Backup  Storage  Keys  Temp
```

- The scripts and the tests create their databases with explicit file paths inside these folders. Nothing relies on SQL Server's own default folders.
- Two environment variables override the defaults for a machine that differs from this one: `ALVERA_DATA_ROOT` (the root, an absolute path) and `ALVERA_SQL_SERVER` (the instance, default `.\SQLEXPRESS`).
- SQL Server's own system databases (`master`, `model`, `msdb`, `tempdb`) stay in its install folder.
- The SQL Server service account must be able to read and write under the root. On this machine the root sits directly under `C:\`, which gives it that access without any extra setting.

## 0. Check the tools (once)

Open **PowerShell** and run:

```powershell
dotnet --version        # expect 10.0.x
dotnet ef --version     # expect 10.0.x (the Entity Framework tool)
node --version          # expect v24.x
sqlcmd -S ".\SQLEXPRESS" -E -Q "SELECT @@VERSION"      # expect SQL Server 2025 (17.0)
```

All four must print something. If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef`. If `sqlcmd` cannot connect, check that the Windows service `SQL Server (SQLEXPRESS)` is running.

## 1. Go to the project folder in PowerShell

The project folder's name contains square brackets (`AI-Project-[Alveara-Dental]`). PowerShell treats `[ ]` as a wildcard, so a plain `cd` fails with *"The specified wildcard character pattern is not valid"*. **Always use `-LiteralPath`**:

```powershell
Set-Location -LiteralPath "C:\Users\firas\Documents\AI-Project-[Alveara-Dental]"
```

The scripts below handle the brackets themselves. If PowerShell refuses to run a script because of the execution policy, run `Set-ExecutionPolicy -Scope Process Bypass` once in that window.

## 2. Make sure nothing old is running

Only one API can use port 5072 and one client port 5173. Check:

```powershell
Get-NetTCPConnection -LocalPort 5072,5173 -State Listen -ErrorAction SilentlyContinue | Select-Object LocalPort, OwningProcess
```

No output means nothing is running. If something is listed, press **Ctrl+C** in the window that runs it, or:

```powershell
Get-NetTCPConnection -LocalPort 5072,5173 -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
```

## 3. (Optional) Start from a clean database

Skip this the first time, or to keep the data you have. To delete the dev database and everything in it:

```powershell
.\scripts\Initialize-DevDatabase.ps1 -Reset
```

It asks you to confirm and then drops `AlveraDev` **and its files**: every user, patient and record in it is lost. It can only ever drop `AlveraDev`.

## 4. Create the database and apply the migrations

```powershell
.\scripts\Initialize-DevDatabase.ps1
```

It creates `AlveraDev` with its files in `C:\.alvera-data\Dev-Data\Database` (if it does not exist yet), builds the API, and applies every migration (93 tables, 42 migrations at the time of writing). It ends with `AlveraDev is ready on .\SQLEXPRESS.` Running it again changes nothing.

## 5. Start the API (window A)

Open a **new PowerShell window**, leave it open, go to the project folder (step 1) and run:

```powershell
.\scripts\Start-DevApi.ps1
```

It sets, for this window only, the connection string (Windows authentication, no password), the storage, key-ring and backup folders under `Dev-Data`, the one-time bootstrap secret and a higher sign-in rate limit (500 attempts a minute instead of 10, so signing in as several users in a row is not locked out), and then runs the API on `http://localhost:5072`. It refuses to start if the port is taken or if `AlveraDev` does not exist. Wait for `Now listening on: http://localhost:5072`. A warning *"Failed to determine the https port for redirect"* is harmless in development.

Check it from another window:

```powershell
Invoke-WebRequest http://localhost:5072/api/health -UseBasicParsing | Select-Object StatusCode   # expect 200
```

## 6. Create the first administrator (once per new database)

There is no screen for this; it is a one-time call to the API, protected by the bootstrap secret. In a second window, in the project folder:

```powershell
.\scripts\New-DevAdmin.ps1
```

It creates the user `admin` with the password `AlveraDev-Admin-1!` and the role `Admin`. Running it again says the first administrator already exists and changes nothing. A wrong secret is reported as such. To use another name or password, give `-Username` and `-Password`.

## 7. Start the client (window B)

```powershell
.\scripts\Start-DevClient.ps1
```

It installs the npm dependencies the first time, then starts the web client. Wait for `Local: http://localhost:5173/`.

## 8. Use it

Open **http://localhost:5173** in your browser and sign in with `admin` / `AlveraDev-Admin-1!`.

A fresh database contains only that administrator. Patients, scheduling and the clinical and billing screens need a practice set up first (a location, an operatory, an appointment type, a provider with working hours) and users for the other roles (dentist, hygienist, assistant, front desk, billing, office manager). Self-registered accounts start disabled and without a role; an administrator enables them and assigns the role in the security administration screen. **Keep the `admin` account as Admin**: if you change its role you lose your administrator and must reset the database (step 3 onwards).

## Stopping

Press **Ctrl+C** in window A (API) and window B (client). Closing the windows also stops them. The database and its files stay: all your data is still on disk.

## Restarting with your data kept

Do **steps 1, 2, 5 and 7 only**. Do not reset, and do not run `New-DevAdmin.ps1` again. After pulling new code run `.\scripts\Initialize-DevDatabase.ps1` first: it applies any new migrations and keeps your data.

## Resetting to a clean database

Repeat steps **2, 3, 4, 5, 6, 7** in order.

## The automated tests, and cleaning up after them

- **Backend tests** (`dotnet test` in `src\Alveara.Api.Tests`) create one database per test class named `AlveraTest_<id>`, with its files in `Test-Data\Database`, and drop it when the class ends. Their backups, document storage, key ring and scratch files are under `Test-Data` too. They never touch `Dev-Data`.
- **Browser walkthroughs** are run with `.\scripts\Invoke-RealBackendWalkthrough.ps1 -Config <config>` (see `docs\testing\REAL_BACKEND_E2E.md`). Each runs on its own fresh database and removes everything afterwards. They need ports 5072 and 5173, so stop the dev API and client first.
- **One setting the backend tests need:** a few tests create a SQL-authentication login and connect with a username and password. The SQL Server instance must therefore allow SQL Server authentication as well as Windows (mixed mode). To turn it on, in PowerShell **as Administrator**: `Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQLServer" -Name LoginMode -Value 2` and `Restart-Service 'MSSQL$SQLEXPRESS' -Force`. This does not enable the `sa` account.
- **After a crashed or interrupted run**, anything left behind is under `Test-Data`. See what would be removed, then remove it:

```powershell
.\scripts\Clear-TestData.ps1 -WhatIf     # only shows what it would do
.\scripts\Clear-TestData.ps1             # asks, then drops the Test-Data databases and deletes the files
```

  It never touches `Dev-Data`. A database with any file outside `Test-Data` is skipped and reported, never dropped.
- **Is everything still under the one root?**

```powershell
.\scripts\Test-DataLocations.ps1
```

  It changes nothing: it lists any database with a file outside `C:\.alvera-data` and exits with 1 if it finds one.

## When something goes wrong

| What you see | Cause and fix |
|---|---|
| `The specified wildcard character pattern is not valid` | The `[ ]` in the folder name. Use `Set-Location -LiteralPath "…"`, never a plain `cd` with the full path. |
| `Port 5072 is already in use` | An API is already running. Step 2. |
| `AlveraDev does not exist` | Step 4 was not done. Run `.\scripts\Initialize-DevDatabase.ps1`. |
| `Cannot open database "AlveraDev"` or a login failure | SQL Server is stopped (start the Windows service `SQL Server (SQLEXPRESS)`), or the database is missing (step 4). |
| `Directory lookup for the file ... failed` or `Access is denied` while creating a database or a backup | The folder does not exist, or the SQL Server service account cannot write there. Keep the data root where the service account has access (the default root under `C:\` does). |
| Browser shows the page but every action fails | The API (window A) is not running, or is not on 5072. Check the health URL in step 5. |
| `bootstrap_already_used` / "already exists" | The database already has its first administrator. Use it, or reset (step 3 onwards). |
| `invalid_bootstrap_secret` | The secret differs from the one the API was started with. The two scripts share a default; pass the same `-BootstrapSecret` to both if you change it. |
| Sign-in refused with "too many attempts" | The sign-in rate limit. `Start-DevApi.ps1` raises it to 500; an API started another way allows 10 a minute. |
| Backend tests with a SQL login fail | Mixed mode is off. See "One setting the backend tests need" above. |
| Port 5173 busy | An old client is running. Step 2. |
| Blank page after an update | Stop the client, run `npm install` in `src\alveara-client`, start it again. |

## Where the code is

API: `src\Alveara.Api`. Client: `src\alveara-client`. Backend tests: `src\Alveara.Api.Tests`. Scripts: `scripts\`. Production deployment (a Windows installer, upgrade and rollback) is planned later (item 52) and is not covered here.
