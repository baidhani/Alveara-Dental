using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(LocalClientCorsPolicy);

app.UseAuthorization();

app.MapControllers();

// N002-R02-01: this is what makes "a LAN client can use the production shell with no public
// internet" a real, single-process topology rather than an API-only demonstration. The built
// React client (npm run build's dist/) is served as static files from this same Kestrel process,
// with an SPA fallback so client-side routes (e.g. /system-status) resolve to index.html on a
// direct request or refresh. In development the client is normally served separately by Vite
// (see vite.config.ts's dev proxy); this path only activates when a build actually exists, so it
// never masks the dev workflow.
var clientBuildPath = builder.Configuration["ClientBuildPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "..", "alveara-client", "dist");
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
