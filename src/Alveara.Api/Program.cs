using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
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

// Measurement events.
builder.Services.AddScoped<IMeasurementEventSink, MeasurementEventSink>();

// Durable background-work mechanism.
builder.Services.AddScoped<IBackgroundJobQueue, BackgroundJobQueue>();
builder.Services.AddScoped<BackgroundJobRunner>();
builder.Services.AddSingleton<BackgroundJobRunnerHeartbeat>();
builder.Services.AddHostedService<BackgroundJobHostedService>();

// STORY-001: authentication + RBAC. Cookie-based session, since the client is served
// same-origin (see the static-file-serving block below) — no bearer-token plumbing needed for a
// LAN-only app. The default challenge/forbid behavior redirects to a login *page*, which makes
// no sense for a JSON API, so both are overridden to return plain status codes instead.
builder.Services.AddScoped<AccountService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
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
    });
builder.Services.AddAuthorization();

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
