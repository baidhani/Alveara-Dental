using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Permissions;

/// <summary>The stored policy rows read together (the caller brackets the read with the policy version).</summary>
public sealed record PolicyRows(IReadOnlyList<RolePermissionOverride> Overrides, IReadOnlyList<PermissionDelegation> Delegations);

/// <summary>Where the store reads the policy from. A seam so the outage and race behaviour can be proven with a controlled source; production uses <see cref="SqlPolicyDataSource"/>.</summary>
public interface IPolicyDataSource
{
    Task<long> ReadVersionAsync(CancellationToken ct);
    Task<PolicyRows> ReadRowsAsync(CancellationToken ct);
}

/// <summary>Reads the policy from SQL Server through a short-lived scope. Every read has an explicit timeout (5 s); a failure is not caught here, the store decides what an unreadable store means.</summary>
public sealed class SqlPolicyDataSource(IServiceScopeFactory scopes) : IPolicyDataSource
{
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    public async Task<long> ReadVersionAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReadTimeout);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
        db.Database.SetCommandTimeout(ReadTimeout);
        return await db.PermissionPolicyStates.AsNoTracking().Select(s => s.Version).SingleAsync(timeout.Token);
    }

    public async Task<PolicyRows> ReadRowsAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReadTimeout);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
        db.Database.SetCommandTimeout(ReadTimeout);
        var overrides = await db.RolePermissionOverrides.AsNoTracking().ToListAsync(timeout.Token);
        var delegations = await db.PermissionDelegations.AsNoTracking().ToListAsync(timeout.Token);
        return new PolicyRows(overrides, delegations);
    }
}

/// <summary>The policy store could not be read and the last verified snapshot is too old (or there is none): authorization is denied (503, <c>permission_policy_unavailable</c>), never answered from the defaults.</summary>
public sealed class PolicyUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A snapshot and whether it is being served stale (the store could not be read, within the 60 second allowance).</summary>
public sealed record PolicyRead(PolicySnapshot Snapshot, bool Stale);

/// <summary>
/// ALV-N017: the process-wide holder of the current <see cref="PolicySnapshot"/>. Reader protocol (docs/design/ADMIN_GRANTABLE_PERMISSIONS_PROPOSAL.md section 3.5): read the policy version; if it equals the
/// snapshot's, serve the snapshot (and note it was verified now); if it differs, wait for ONE shared reload (single flight, bounded wait of 2 s) and serve the new snapshot, never the old one. The reload reads the
/// version, then the rows, then the version again and repeats (at most 3 attempts) if a writer got in between, then publishes the built snapshot by one atomic reference swap. If the store cannot be read, the last
/// snapshot may serve for at most 60 seconds counted from the last time it was confirmed current; after that, or with no snapshot, the answer is <see cref="PolicyUnavailableException"/>. The defaults are never
/// used as a fallback while stored rows may exist: they come only from a successful read that found no rows.
/// </summary>
public sealed class PermissionPolicyStore(IPolicyDataSource source, TimeProvider time, ILogger<PermissionPolicyStore> logger, IPolicyObserver? observer = null)
{
    public static readonly TimeSpan MaxStaleness = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ReloadWait = TimeSpan.FromSeconds(2);
    public const int MaxReloadAttempts = 3;

    private readonly object _gate = new();
    private PolicySnapshot? _current;
    private long _verifiedAt;
    private Task<PolicySnapshot>? _reload;
    private long _lastWarnAt;
    private int _reloads;

    /// <summary>How many snapshots have been built (a single-flight burst counts once).</summary>
    public int ReloadCount => Volatile.Read(ref _reloads);

    /// <summary>The snapshot currently held, if any (for health and tests; decisions use <see cref="GetAsync"/>).</summary>
    public PolicySnapshot? Current => Volatile.Read(ref _current);

    /// <summary>The policy version a request already read (see <see cref="PolicyVersionHandoff"/>) may be passed in so the store does not read it again; null reads it.</summary>
    public async Task<PolicyRead> GetAsync(CancellationToken ct, long? versionAlreadyRead = null)
    {
        var held = Volatile.Read(ref _current);
        long version;
        try
        {
            version = versionAlreadyRead ?? await source.ReadVersionAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Outage(held, ex);
        }

        if (held is not null && held.Version == version)
        {
            Volatile.Write(ref _verifiedAt, time.GetTimestamp());
            return new PolicyRead(held, false);
        }

        try
        {
            return new PolicyRead(await ReloadAsync(version, ct), false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Outage(held, ex);
        }
    }

    private Task<PolicySnapshot> ReloadAsync(long knownVersion, CancellationToken ct)
    {
        Task<PolicySnapshot> flight;
        lock (_gate)
        {
            // a reload that finished after this request read the version has already published what it needs: serve that, do not read the store again
            if (Volatile.Read(ref _current) is { } published && published.Version >= knownVersion) return Task.FromResult(published);
            _reload ??= RunReloadAsync(knownVersion);
            flight = _reload;
        }
        return flight.WaitAsync(ReloadWait, ct);
    }

    /// <summary>The version the caller just read opens the bracket (it was read before the rows, so the bracket is still sound); later attempts read their own opening version.</summary>
    private async Task<PolicySnapshot> RunReloadAsync(long knownVersion)
    {
        try
        {
            await Task.Yield();                                                                                // never run the read inside the caller's lock
            for (var attempt = 1; attempt <= MaxReloadAttempts; attempt++)
            {
                var before = attempt == 1 ? knownVersion : await source.ReadVersionAsync(CancellationToken.None);
                var rows = await source.ReadRowsAsync(CancellationToken.None);
                var after = await source.ReadVersionAsync(CancellationToken.None);
                if (before != after)                                                                           // a writer got in between: the rows may belong to neither version, read again
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(5 * attempt));                                  // a short pause lets a burst of writes settle
                    continue;
                }
                var snapshot = PolicySnapshotBuilder.Build(before, rows.Overrides, rows.Delegations);
                Volatile.Write(ref _current, snapshot);                                                        // one atomic swap
                Volatile.Write(ref _verifiedAt, time.GetTimestamp());
                Interlocked.Increment(ref _reloads);
                Report(snapshot);                                                                              // invalid rows are reported off the decision path
                return snapshot;
            }
            throw new PolicyUnavailableException("The policy kept changing while it was being read.");
        }
        finally
        {
            lock (_gate) _reload = null;
        }
    }

    private readonly object _reportGate = new();
    private PolicySnapshot? _pendingReport;
    private bool _reporting;
    private Task _lastReport = Task.CompletedTask;

    /// <summary>Completes when the reporting started so far has finished (tests wait on it; nothing in production does).</summary>
    public Task ReportsIdle() { lock (_reportGate) return _lastReport; }

    /// <summary>Hands the snapshot to the observer in the background. At most one report runs at a time and only the newest snapshot waiting is reported, so a burst of policy changes cannot pile up work.</summary>
    private void Report(PolicySnapshot snapshot)
    {
        if (observer is null || (snapshot.InvalidRows.Count == 0 && !snapshot.Degraded)) return;
        lock (_reportGate)
        {
            _pendingReport = snapshot;
            if (_reporting) return;
            _reporting = true;
            _lastReport = Task.Run(async () =>
            {
                while (true)
                {
                    PolicySnapshot? next;
                    lock (_reportGate)
                    {
                        next = _pendingReport;
                        _pendingReport = null;
                        if (next is null) { _reporting = false; return; }
                    }
                    try { await observer.OnPublishedAsync(next, CancellationToken.None); }
                    catch (Exception ex) { logger.LogWarning("permission_policy_report_failed error_class={ErrorClass}", ex.GetType().Name); }
                }
            });
        }
    }

    private PolicyRead Outage(PolicySnapshot? held, Exception cause)
    {
        var verifiedAt = Volatile.Read(ref _verifiedAt);
        var withinAllowance = held is not null && verifiedAt != 0 && time.GetElapsedTime(verifiedAt) <= MaxStaleness;
        Warn(cause, withinAllowance);
        if (withinAllowance) return new PolicyRead(held!, true);
        throw new PolicyUnavailableException("The permission policy cannot be read and no recent verified copy exists.", cause);
    }

    /// <summary>Logs the error class and what was done, at most once every 5 seconds (an outage must not flood the log); never a row, a role or a message.</summary>
    private void Warn(Exception cause, bool servedStale)
    {
        var now = time.GetTimestamp();
        var last = Volatile.Read(ref _lastWarnAt);
        if (last != 0 && time.GetElapsedTime(last, now) < TimeSpan.FromSeconds(5)) return;
        Volatile.Write(ref _lastWarnAt, now);
        logger.LogWarning("permission_policy_unreadable error_class={ErrorClass} served_stale={ServedStale}", cause.GetType().Name, servedStale);
    }
}
