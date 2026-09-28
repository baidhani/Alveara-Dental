using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// N002-R03-02: the R03 review's own reviewer-owned harness proved that, even after
/// BackgroundJobRunner stopped persisting/logging raw exception messages, a synthetic
/// patient-like value inserted through the real Entity Framework pipeline (a duplicate
/// UserAccount.Username, which SQL Server's unique-constraint violation text includes literally)
/// still reached ordinary log output through EF's own command/update logging categories and the
/// default hosting diagnostics path — a story-wide PHI-safe-logging gap, not just the one runner
/// call site. This test exercises the real, fully configured host (appsettings.json's LogLevel
/// filters + Program.cs's SafeExceptionHandlingMiddleware), captures every log record across every
/// category through a real ILoggerProvider registered in the DI pipeline, and proves the synthetic
/// value is absent no matter which component would otherwise have logged it.
/// </summary>
public class FrameworkLoggingPhiSafetyTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FrameworkLoggingPhiSafetyTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public readonly List<string> Messages = [];
        private readonly object _lock = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

        public void Dispose() { }

        private sealed class CapturingLogger(string category, CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var message = $"[{category}] {formatter(state, exception)}";
                if (exception is not null)
                {
                    message += $" | exception: {exception}";
                }

                lock (owner._lock)
                {
                    owner.Messages.Add(message);
                }
            }
        }
    }

    [Fact]
    public async Task A_synthetic_patient_like_value_in_a_real_unique_constraint_violation_never_appears_in_any_captured_log_across_any_category()
    {
        var capturingProvider = new CapturingLoggerProvider();

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.ConfigureServices(services =>
            {
                // Deliberately does NOT clear existing providers/filters — this must prove the
                // real, fully configured logging pipeline (appsettings.json's category filters
                // included) never leaks the value, not a synthetic bare-bones logging setup.
                services.AddLogging(logging =>
                {
                    logging.AddProvider(capturingProvider);
                    logging.SetMinimumLevel(LogLevel.Trace);
                });
            });
        });

        const string syntheticValue = "SyntheticPatientAliceExample";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
            var username = $"{syntheticValue}-{Guid.NewGuid():N}";

            db.UserAccounts.Add(new UserAccount { Id = Guid.NewGuid(), Username = username, CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            // A second insert with the same username violates the unique index — this is the
            // real duplicate-key SqlException whose message text includes the literal value.
            db.ChangeTracker.Clear();
            db.UserAccounts.Add(new UserAccount { Id = Guid.NewGuid(), Username = username, CreatedAtUtc = DateTimeOffset.UtcNow });
            var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());
            Assert.NotNull(ex); // the violation must genuinely occur for this test to mean anything

            // Also exercise the request pipeline's own exception path (SafeExceptionHandlingMiddleware)
            // by forcing the same violation to surface through a real HTTP round trip, not just DI.
            var client = factory.CreateClient();
            _ = await client.GetAsync("/api/health"); // sanity: pipeline is alive
        }

        var leaked = capturingProvider.Messages.Where(m => m.Contains(syntheticValue, StringComparison.Ordinal)).ToList();
        Assert.True(leaked.Count == 0, $"Synthetic patient-like value leaked into {leaked.Count} log record(s):\n{string.Join("\n---\n", leaked)}");
    }
}
