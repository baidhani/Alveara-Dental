using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

const string LocalClientCorsPolicy = "LocalClientCorsPolicy";

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// The client is a separate Vite dev server during development. Production
// deployment serves the built client from the same origin as this API, at
// which point this policy is not exercised, but it is kept scoped to
// localhost origins only rather than a wildcard.
builder.Services.AddCors(options =>
{
    options.AddPolicy(LocalClientCorsPolicy, policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// --- ALV-N002: Core architecture wiring ---

// Server-owned database: this connection string exists only in this process's configuration and
// is never sent to the client. See tests/no-db-credentials-in-client-bundle for the check that
// enforces this.
var connectionString = builder.Configuration.GetConnectionString("Alveara")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=AlveraDev;Trusted_Connection=True;TrustServerCertificate=True";
builder.Services.AddDbContext<AlveraDbContext>(options => options.UseSqlServer(connectionString));

// Practice time zone — configurable, defaults to America/Chicago. All appointment-local
// conversions go through this, never the server's own local time zone.
var practiceTimeZoneId = builder.Configuration["PracticeTimeZone"] ?? "America/Chicago";
builder.Services.AddSingleton<IPracticeClock>(
    _ => new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById(practiceTimeZoneId)));

// Storage-root/blob abstraction seam (no document module yet — ALV-N010 consumes this).
var storageRoot = builder.Configuration["StorageRoot"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "blobs");
builder.Services.AddSingleton<IBlobStorage>(_ => new LocalDiskBlobStorage(storageRoot));

// ALV-N003: practice/staff/provider/operatory/appointment-type/availability configuration, and
// the read model scheduling consumes.
builder.Services.AddScoped<PracticeConfigurationService>();
builder.Services.AddScoped<StaffProviderService>();
builder.Services.AddScoped<SchedulingConfiguration>();
builder.Services.AddScoped<Alveara.Api.Architecture.Patients.PatientDuplicateDetector>();
builder.Services.AddScoped<Alveara.Api.Architecture.Patients.PatientRegistrationService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Patients.PatientEditService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Patients.PatientRelationshipService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Patients.PatientDirectory>();
builder.Services.AddScoped<Alveara.Api.Architecture.Patients.PatientRegistrationSettingsService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Forms.FormTemplateService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Forms.PatientFormService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Forms.PatientFormReader>();
builder.Services.AddScoped<Alveara.Api.Architecture.Forms.CheckInReadinessService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Clinical.EncounterService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Clinical.EncounterReader>();
builder.Services.AddScoped<Alveara.Api.Architecture.Clinical.ClinicalRecordService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Clinical.ClinicalRecordReader>();
builder.Services.AddScoped<Alveara.Api.Architecture.Clinical.EncounterNoteService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Clinical.EncounterVitalsService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Clinical.EncounterSigningService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Clinical.NoteTemplateService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Safety.SafetyContextService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Safety.ISafetyContextProvider>(sp => sp.GetRequiredService<Alveara.Api.Architecture.Safety.SafetyContextService>());
builder.Services.AddScoped<Alveara.Api.Architecture.Safety.SafetyAlertService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Odontogram.OdontogramService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Periodontal.PerioService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Odontogram.ConditionTypeService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Safety.ClearanceService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Scheduling.AppointmentScheduler>();
builder.Services.AddScoped<Alveara.Api.Architecture.Scheduling.AppointmentManager>();
builder.Services.AddScoped<Alveara.Api.Architecture.Scheduling.AppointmentFlowService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Scheduling.VisitAssignmentService>();
builder.Services.AddScoped<Alveara.Api.Architecture.Scheduling.VisitBoardService>();

// Measurement events.
builder.Services.AddScoped<IMeasurementEventSink, MeasurementEventSink>();

// Durable background-work mechanism.
builder.Services.AddScoped<IBackgroundJobQueue, BackgroundJobQueue>();
builder.Services.AddScoped<BackgroundJobRunner>();
builder.Services.AddSingleton<BackgroundJobRunnerHeartbeat>();
builder.Services.AddHostedService<BackgroundJobHostedService>();

// STORY-001/ALV-001-C01: authentication + RBAC. Cookie-based session, since the client is served
// same-origin (see the static-file-serving block below) — no bearer-token plumbing needed for a
// LAN-only app. The default challenge/forbid behavior redirects to a login *page*, which makes
// no sense for a JSON API, so both are overridden to return plain status codes instead.
builder.Services.AddScoped<AccountService>();

// Data Protection backs both the MFA-secret-at-rest protector and the short-lived MFA challenge
// token. Keys are persisted to disk (not the default in-memory/user-profile location) so a
// restart doesn't invalidate every enrolled MFA secret or in-flight challenge.
var dataProtectionKeysPath = builder.Configuration["DataProtectionKeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
var dataProtection = builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
// ALV-N004 R02 (review finding ALV-N004-R01-03): the application name is deliberately NOT forced. Existing MFA secrets
// were protected under the framework's default discriminator (the content-root path), and changing it would make
// them unreadable. An installation that must survive a move to another folder/server sets DataProtection:ApplicationName
// (backups record the effective value, and a restore says exactly what to set); leaving it unset keeps the legacy
// default, so existing payloads keep working untouched.
var dataProtectionApplicationName = builder.Configuration["DataProtection:ApplicationName"];
if (!string.IsNullOrWhiteSpace(dataProtectionApplicationName)) dataProtection.SetApplicationName(dataProtectionApplicationName);

// ALV-N004: encrypted full-state backup, verification, restore and recovery. Every on-disk location
// is server-owned configuration (never user input).
var backupRoot = builder.Configuration["Backup:Root"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "backup");
var backupPaths = new BackupPaths(
    DefaultBackupDirectory: builder.Configuration["Backup:Directory"] ?? Path.Combine(backupRoot, "sets"),
    StagingRoot: builder.Configuration["Backup:StagingRoot"] ?? Path.Combine(backupRoot, "staging"),
    RestoreRoot: builder.Configuration["Backup:RestoreRoot"] ?? Path.Combine(backupRoot, "restore"),
    NotificationDirectory: builder.Configuration["Backup:NotificationDirectory"] ?? Path.Combine(backupRoot, "notifications"),
    ConnectionString: connectionString,
    DatabaseName: new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).InitialCatalog,
    ImportDirectory: builder.Configuration["Backup:ImportDirectory"] ?? Path.Combine(backupRoot, "import"));
builder.Services.AddSingleton(backupPaths);
builder.Services.AddSingleton<IBackupSnapshotProvider>(_ => new SqlServerBackupSnapshotProvider(connectionString, backupPaths.DatabaseName));
builder.Services.AddSingleton<IDatabaseRestoreProvider>(_ => new SqlServerRestoreProvider(connectionString));
builder.Services.AddSingleton<IBackupNotifier, FileDropBackupNotifier>();
builder.Services.AddSingleton<IDeploymentSettingsProvider, RuntimeDeploymentSettingsProvider>();
builder.Services.AddSingleton<DeploymentInvariantStatus>();
builder.Services.AddHostedService<DeploymentInvariantMonitor>();
builder.Services.AddSingleton<IBackupAssetSource, DeploymentConfigurationSource>();
// One source per persistent asset class except the database (handled with SQL Server's own BACKUP).
// A later story that adds a new on-disk store registers another IBackupAssetSource and adds its
// class to ManagedAssetClasses.
builder.Services.AddSingleton<IBackupAssetSource>(_ => new FileTreeAssetSource(ManagedAssetClasses.Documents, storageRoot, relative => relative.Contains(".tmp-", StringComparison.Ordinal)));
builder.Services.AddSingleton<IBackupAssetSource>(_ => new FileTreeAssetSource(ManagedAssetClasses.DataProtectionKeys, dataProtectionKeysPath));
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<BackupRestoreService>();
builder.Services.AddScoped<BackupScheduler>();
builder.Services.AddScoped<IBackgroundJobHandler, ScheduledBackupJobHandler>();
builder.Services.AddHostedService<BackupSchedulerHostedService>();

// CSRF protection for cookie-authenticated state-changing calls (ALV-001-C01 requirement). A
// double-submit pattern: the client reads the token from a non-HttpOnly cookie the antiforgery
// middleware sets and echoes it back in this header on any state-changing request.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = "Alveara-CSRF";
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // Deployment-appropriate cookie attributes: HttpOnly is the ASP.NET Core cookie-auth
        // default already; Secure and SameSite are set explicitly here rather than left implicit.
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest // dev also runs over plain HTTP on localhost
            : CookieSecurePolicy.Always;

        // ALV-N009 R03 (review finding ALV-N009-R02-01): explicitly non-sliding. The default
        // (true) silently renews ExpiresUtc on any authenticated request once past the renewal
        // midpoint - including background client polling that exists purely to *observe* session
        // state (see AuthContext.tsx's revalidation poll), not to represent genuine user activity.
        // With sliding renewal on, that observation traffic itself would keep an otherwise-idle
        // session alive indefinitely, defeating the admin-configured SessionTimeoutMinutes. Fixed
        // ExpiresUtc (set once at sign-in in SignInAsync, below) makes the configured timeout an
        // honest, deterministic deadline regardless of what polls or observes the session.
        options.SlidingExpiration = false;

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        // ALV-001-C01 "explicit sign-out/revocation": a session's SecurityStamp claim is checked
        // against the account's current value on every request. Rotating the stamp (password
        // reset, role change, disable, MFA change, or an explicit "revoke all sessions" call)
        // invalidates every cookie issued before the rotation — real server-side revocation
        // without a server-side session store.
        options.Events.OnValidatePrincipal = async context =>
        {
            var stampClaim = context.Principal?.FindFirst("security_stamp")?.Value;
            var userIdClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (stampClaim is null || userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            {
                context.RejectPrincipal();
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<AlveraDbContext>();
            var currentStamp = await db.UserAccounts
                .Where(u => u.Id == userId)
                .Select(u => (Guid?)u.SecurityStamp)
                .SingleOrDefaultAsync();

            if (currentStamp is null || currentStamp.Value.ToString() != stampClaim)
            {
                context.RejectPrincipal();
            }
        };
    });
builder.Services.AddAuthorization();

// ALV-001-C01 "rate-limit/throttle repeated authentication attempts": distinct from per-account
// lockout (AccountService.LoginAsync) - this throttles by caller IP address, so it also protects
// register/bootstrap/mfa-challenge/reset-password against distributed attempts spread across many
// different usernames, which per-account lockout alone cannot. Sliding window, no queueing (an
// over-limit caller is rejected immediately with 429, never made to wait server-side).
var authAttemptsPermitLimit = builder.Configuration.GetValue<int?>("AuthAttemptRateLimit:PermitLimit") ?? 10;
var authAttemptsWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("AuthAttemptRateLimit:WindowSeconds") ?? 60);
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("AuthAttempts", httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = authAttemptsPermitLimit,
            Window = authAttemptsWindow,
            SegmentsPerWindow = 4,
            QueueLimit = 0,
        }));
});

var app = builder.Build();

// Configure the HTTP request pipeline.

// N002-R03-02: registered first so no unhandled exception (including a raw SQL/EF exception
// carrying literal database values) reaches the built-in developer-exception-page/hosting
// diagnostics logger before this safe summary does.
app.UseMiddleware<Alveara.Api.Architecture.Logging.SafeExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(LocalClientCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();

// ALV-N004 R02: refuse domain API calls while the deployment settings do not match what the data was created under.
app.UseMiddleware<DeploymentGuardMiddleware>();

app.MapControllers();

// N002-R02-01/N002-R03-01: this is what makes "a LAN client can use the production shell with no
// public internet" a real, single-process topology rather than an API-only demonstration. The
// built React client is served as static files from this same Kestrel process, with an SPA
// fallback so client-side routes (e.g. /system-status) resolve to index.html on a direct request
// or refresh.
//
// The default path is `wwwroot` under the content root — the same directory `dotnet publish`
// actually populates (see Alveara.Api.csproj's CopyClientBuildToPublishOutput target), so a real
// published server output serves the shell without any extra manual step. During ordinary
// repository-layout development (no publish, client normally served separately by Vite — see
// vite.config.ts's dev proxy), this also transparently picks up a `dist/` a developer built
// directly into `src/alveara-client/` if `wwwroot` doesn't exist, so nothing about the dev
// workflow breaks. Either way, this path only activates when a build actually exists.
var clientBuildPath = builder.Configuration["ClientBuildPath"];
if (string.IsNullOrEmpty(clientBuildPath))
{
    var wwwroot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
    var repoLayoutDist = Path.Combine(builder.Environment.ContentRootPath, "..", "alveara-client", "dist");
    clientBuildPath = File.Exists(Path.Combine(wwwroot, "index.html")) ? wwwroot : repoLayoutDist;
}
clientBuildPath = Path.GetFullPath(clientBuildPath);

if (Directory.Exists(clientBuildPath) && File.Exists(Path.Combine(clientBuildPath, "index.html")))
{
    var clientFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(clientBuildPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = clientFileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = clientFileProvider });
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = clientFileProvider });
}

app.Run();

// Exposed so the API's test project can spin up an in-memory instance
// without duplicating Program.cs.
public partial class Program { }
