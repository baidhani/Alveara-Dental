using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// ALV-018-C02 slice 4b: one unit of independent work for the appointment commit: its own database context and its own scheduler, so scheduling a row (which opens its own transaction and takes its own locks) and
/// saving its result commit by themselves, apart from the background runner's transaction. Disposing the scope releases both.
/// </summary>
public sealed class ImportWorkScope(AlveraDbContext db, AppointmentScheduler scheduler, IAsyncDisposable? owner = null, BillingService? billing = null) : IAsyncDisposable
{
    public AlveraDbContext Db { get; } = db;
    public AppointmentScheduler Scheduler { get; } = scheduler;
    public BillingService Billing => billing ?? throw new InvalidOperationException("This scope was made without a billing service; an opening-balance commit needs one.");

    public async ValueTask DisposeAsync()
    {
        if (owner is not null) await owner.DisposeAsync();
    }
}

/// <summary>Hands out independent scopes of work. A seam so the commit can be tested with real contexts without a running host.</summary>
public interface IImportWorkScopeFactory
{
    ImportWorkScope Create();
}

/// <summary>The real factory: a new dependency-injection scope for each unit of work, as a background job handler must (see also <c>ScheduledBackupJobHandler</c>).</summary>
public sealed class ImportWorkScopeFactory(IServiceScopeFactory scopes) : IImportWorkScopeFactory
{
    public ImportWorkScope Create()
    {
        var scope = scopes.CreateAsyncScope();
        return new ImportWorkScope(scope.ServiceProvider.GetRequiredService<AlveraDbContext>(), scope.ServiceProvider.GetRequiredService<AppointmentScheduler>(), scope, scope.ServiceProvider.GetRequiredService<BillingService>());
    }
}
