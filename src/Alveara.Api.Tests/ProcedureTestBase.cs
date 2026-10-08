using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>Shared helpers for the ALV-N005 procedure catalog tests: a service over a real SQL Server database, a fixed practice clock, and a valid input to start from.</summary>
public abstract class ProcedureTestBase : SafetyTestBase
{
    /// <summary>A fixed "today" (2030-03-15 in the practice's time zone) so dates in the tests do not depend on when they run.</summary>
    protected static readonly TestClock Mar15 = new(new DateTimeOffset(2030, 3, 15, 15, 0, 0, TimeSpan.Zero));
    protected static readonly TestClock Jun10 = new(new DateTimeOffset(2030, 6, 10, 15, 0, 0, TimeSpan.Zero));
    protected static readonly DateOnly Today = new(2030, 3, 15);

    protected ProcedureCatalogService Service(AlveraDbContext db, IPracticeClock clock, params IProcedureUsageSource[] extraSources) =>
        new(db, clock, [new FindingLinkProcedureUsageSource(db), .. extraSources]);

    protected Task<T> With<T>(Func<ProcedureCatalogService, Task<T>> action, IPracticeClock? clock = null, params IProcedureUsageSource[] extraSources) =>
        WithDb(db => action(Service(db, clock ?? Mar15, extraSources)));

    protected static ProcedureInput Input(string code = "LOCAL-100", string system = "Local", string description = "Office visit", string category = "Diagnostic", string scope = "WholeMouth",
        string? dentition = null, decimal? fee = 50m, string? sourceName = null, string? sourceVersion = null, DateOnly? effectiveFrom = null, DateOnly? validThrough = null) =>
        new(system, code, description, category, scope, dentition, fee, sourceName, sourceVersion, effectiveFrom, validThrough);

    protected Task<ProcedureDetailView> CreateAsync(ProcedureInput? input = null, IPracticeClock? clock = null, Guid? actor = null) =>
        With(s => s.CreateAsync(input ?? Input(), actor ?? S.Actor, default), clock);

    protected Task<ProcedureDetailView> ReviseAsync(ProcedureDetailView current, ProcedureInput input, string? reason = "Annual fee review", IPracticeClock? clock = null, string? rowVersion = null) =>
        With(s => s.ReviseAsync(current.Summary.Id, input, reason, rowVersion ?? current.Summary.RowVersion, S.Actor, default), clock);

    protected static async Task<ProcedureCatalogException> Refused<T>(Func<Task<T>> action) => await Xunit.Assert.ThrowsAsync<ProcedureCatalogException>(action);

    /// <summary>The same fields as <paramref name="current"/>'s newest version, ready to change one thing.</summary>
    protected static ProcedureInput Like(ProcedureDetailView current, decimal? fee = null, string? description = null, string? sourceVersion = null, DateOnly? effectiveFrom = null)
    {
        var v = current.Versions[^1];
        return new ProcedureInput(current.Summary.CodeSystem, current.Summary.Code, description ?? v.Description, v.Category, v.Scope, v.Dentition, fee ?? v.Fee, v.SourceName, sourceVersion ?? v.SourceVersion,
            effectiveFrom, v.ValidThrough);
    }
}
