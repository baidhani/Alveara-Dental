using Microsoft.Extensions.Logging.Abstractions;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Tests;

/// <summary>A controlled policy source: a settable version and rows, and a switch that makes every read fail, so the engine's reload, race and outage behaviour can be proven without a database.</summary>
public sealed class ControlledPolicySource : IPolicyDataSource
{
    private long _version;
    public List<RolePermissionOverride> Overrides { get; } = [];
    public List<PermissionDelegation> Delegations { get; } = [];
    public bool Failing { get; set; }
    public int VersionReads;
    public int RowReads;
    /// <summary>Called inside every row read (a test can change the version here to simulate a writer racing the reload).</summary>
    public Action? OnReadRows { get; set; }
    public TimeSpan RowReadDelay { get; set; }

    public long Version { get => Volatile.Read(ref _version); set => Volatile.Write(ref _version, value); }

    public Task<long> ReadVersionAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref VersionReads);
        if (Failing) throw new InvalidOperationException("controlled outage");
        return Task.FromResult(Version);
    }

    public async Task<PolicyRows> ReadRowsAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref RowReads);
        if (Failing) throw new InvalidOperationException("controlled outage");
        if (RowReadDelay > TimeSpan.Zero) await Task.Delay(RowReadDelay, CancellationToken.None);
        var rows = new PolicyRows(Overrides.ToList(), Delegations.ToList());
        OnReadRows?.Invoke();
        return rows;
    }
}

public static class PolicyTestSupport
{
    public static PermissionPolicyStore Store(IPolicyDataSource source, TimeProvider? time = null) =>
        new(source, time ?? TimeProvider.System, NullLogger<PermissionPolicyStore>.Instance);

    /// <summary>An effective-permission service over the code defaults (no stored rows), for tests that only need the seam.</summary>
    public static IEffectivePermissions Defaults() => new EffectivePermissionService(Store(new ControlledPolicySource()));

    public static RolePermissionOverride Override(string role, string permission, string effect = "Revoke") =>
        new() { Id = Guid.NewGuid(), Role = role, Permission = permission, Effect = effect, Reason = "controlled", ChangedByUserId = Guid.NewGuid(), ChangedAtUtc = DateTimeOffset.UtcNow };
}

/// <summary>A clock a test moves by hand, so the 60 second allowance can be proven without waiting.</summary>
public sealed class ManualTime : TimeProvider
{
    private long _ticks = 1_000;
    public override long TimestampFrequency => 1_000;                                                           // one tick per millisecond
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, (long)by.TotalMilliseconds);
}
