using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017, the reader protocol with a controlled source: a warm read costs one version read and nothing else; a changed version is served from a new snapshot, never the old one; a burst on a changed version
/// causes one reload (single flight); a writer racing the reload is detected by the version bracket and the read repeated; an unreadable store is served from the last verified snapshot for at most 60 seconds
/// counted from the last confirmation and then denied; with no snapshot an unreadable store denies; the defaults are used only after a successful read that found no rows.
/// </summary>
public class PermissionPolicyStoreTests
{
    [Fact]
    public async Task A_warm_read_costs_exactly_one_version_read_and_serves_the_same_snapshot()
    {
        var source = new ControlledPolicySource();
        var store = PolicyTestSupport.Store(source);
        var first = (await store.GetAsync(default)).Snapshot;
        var versionReads = source.VersionReads;
        var rowReads = source.RowReads;
        for (var i = 0; i < 20; i++) Assert.Same(first, (await store.GetAsync(default)).Snapshot);
        Assert.Equal(20, source.VersionReads - versionReads);
        Assert.Equal(0, source.RowReads - rowReads);
        Assert.Equal(1, store.ReloadCount);
    }

    [Fact]
    public async Task The_first_read_finding_no_rows_gives_the_code_defaults()
    {
        var snapshot = (await PolicyTestSupport.Store(new ControlledPolicySource()).GetAsync(default)).Snapshot;
        Assert.All(Enum.GetValues<Role>(), r => Assert.True(snapshot.EffectiveFor(r).SetEquals(PermissionMatrix.PermissionsFor(r))));
    }

    [Fact]
    public async Task A_changed_version_is_served_from_the_new_snapshot_never_the_old_one()
    {
        var source = new ControlledPolicySource();
        var store = PolicyTestSupport.Store(source);
        var old = (await store.GetAsync(default)).Snapshot;
        Assert.Contains(Permission.ViewBilling, old.EffectiveFor(Role.Dentist));

        source.Overrides.Add(PolicyTestSupport.Override("Dentist", nameof(Permission.ViewBilling)));
        source.Version = 1;
        var fresh = (await store.GetAsync(default)).Snapshot;
        Assert.Equal(1, fresh.Version);
        Assert.DoesNotContain(Permission.ViewBilling, fresh.EffectiveFor(Role.Dentist));
        Assert.Contains(Permission.ViewBilling, old.EffectiveFor(Role.Dentist));                                      // the old snapshot is untouched: immutable

        source.Overrides.Clear();
        source.Version = 2;                                                                                          // a revert
        Assert.Contains(Permission.ViewBilling, (await store.GetAsync(default)).Snapshot.EffectiveFor(Role.Dentist));
    }

    [Fact]
    public async Task A_burst_of_fifty_requests_on_a_changed_version_causes_one_reload()
    {
        var source = new ControlledPolicySource { RowReadDelay = TimeSpan.FromMilliseconds(150) };
        var store = PolicyTestSupport.Store(source);
        await store.GetAsync(default);
        source.Overrides.Add(PolicyTestSupport.Override("Dentist", nameof(Permission.ViewBilling)));
        source.Version = 1;
        var rowReadsBefore = source.RowReads;
        var reloadsBefore = store.ReloadCount;
        var reads = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => store.GetAsync(default)));
        Assert.All(reads, r => Assert.Equal(1, r.Snapshot.Version));
        Assert.Equal(1, source.RowReads - rowReadsBefore);
        Assert.Equal(1, store.ReloadCount - reloadsBefore);
    }

    [Fact]
    public async Task A_writer_racing_the_reload_is_detected_by_the_version_bracket_and_the_read_repeated()
    {
        var source = new ControlledPolicySource();
        var store = PolicyTestSupport.Store(source);
        await store.GetAsync(default);
        source.Version = 1;
        var raced = false;
        source.OnReadRows = () =>
        {
            if (raced) return;
            raced = true;
            source.Overrides.Add(PolicyTestSupport.Override("Dentist", nameof(Permission.ViewBilling)));          // the writer commits between the reload's two version reads
            source.Version = 2;
        };
        var rowReadsBefore = source.RowReads;
        var snapshot = (await store.GetAsync(default)).Snapshot;
        Assert.Equal(2, snapshot.Version);                                                                           // never a snapshot of rows that belong to neither version
        Assert.DoesNotContain(Permission.ViewBilling, snapshot.EffectiveFor(Role.Dentist));
        Assert.Equal(2, source.RowReads - rowReadsBefore);
    }

    [Fact]
    public async Task A_policy_that_never_stops_changing_during_a_read_is_unavailable_after_three_attempts_and_not_guessed()
    {
        var source = new ControlledPolicySource();
        source.OnReadRows = () => source.Version += 1;                                                                // every read is raced
        var store = PolicyTestSupport.Store(source);
        await Assert.ThrowsAsync<PolicyUnavailableException>(() => store.GetAsync(default));
        Assert.Equal(PermissionPolicyStore.MaxReloadAttempts, source.RowReads);
    }

    [Fact]
    public async Task An_unreadable_store_serves_the_last_verified_snapshot_for_sixty_seconds_then_denies()
    {
        var time = new ManualTime();
        var source = new ControlledPolicySource();
        var store = PolicyTestSupport.Store(source, time);
        var held = (await store.GetAsync(default)).Snapshot;
        source.Failing = true;

        time.Advance(TimeSpan.FromSeconds(59));
        var stale = await store.GetAsync(default);
        Assert.True(stale.Stale);
        Assert.Same(held, stale.Snapshot);

        time.Advance(TimeSpan.FromSeconds(2));                                                                       // 61 s since the last confirmation
        await Assert.ThrowsAsync<PolicyUnavailableException>(() => store.GetAsync(default));

        source.Failing = false;                                                                                      // the store recovers: the next read is current again
        var back = await store.GetAsync(default);
        Assert.False(back.Stale);
    }

    [Fact]
    public async Task The_sixty_seconds_count_from_the_last_confirmation_not_from_when_the_snapshot_was_loaded()
    {
        var time = new ManualTime();
        var source = new ControlledPolicySource();
        var store = PolicyTestSupport.Store(source, time);
        await store.GetAsync(default);
        time.Advance(TimeSpan.FromSeconds(300));
        await store.GetAsync(default);                                                                               // confirmed current at t=300
        source.Failing = true;
        time.Advance(TimeSpan.FromSeconds(50));
        Assert.True((await store.GetAsync(default)).Stale);                                                          // 50 s after the confirmation, though loaded 350 s ago
        time.Advance(TimeSpan.FromSeconds(11));
        await Assert.ThrowsAsync<PolicyUnavailableException>(() => store.GetAsync(default));
    }

    [Fact]
    public async Task With_no_snapshot_and_an_unreadable_store_the_answer_is_deny_never_the_defaults()
    {
        var source = new ControlledPolicySource { Failing = true };
        var store = PolicyTestSupport.Store(source);
        await Assert.ThrowsAsync<PolicyUnavailableException>(() => store.GetAsync(default));
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task A_stale_snapshot_is_never_used_for_a_version_it_knows_has_moved_once_the_allowance_is_over()
    {
        var time = new ManualTime();
        var source = new ControlledPolicySource();
        var store = PolicyTestSupport.Store(source, time);
        await store.GetAsync(default);
        source.Version = 1;
        source.Overrides.Add(PolicyTestSupport.Override("Dentist", nameof(Permission.ViewBilling)));
        source.Failing = true;                                                                                       // the revoke was committed, but this host can no longer read the store
        time.Advance(TimeSpan.FromSeconds(61));
        await Assert.ThrowsAsync<PolicyUnavailableException>(() => store.GetAsync(default));
    }

    [Fact]
    public async Task A_slow_reload_is_not_waited_for_more_than_two_seconds_and_falls_back_to_the_outage_rule()
    {
        var time = new ManualTime();
        var source = new ControlledPolicySource { RowReadDelay = TimeSpan.FromSeconds(4) };
        var store = PolicyTestSupport.Store(source, time);
        source.RowReadDelay = TimeSpan.Zero;
        var held = (await store.GetAsync(default)).Snapshot;
        source.Version = 1;
        source.RowReadDelay = TimeSpan.FromSeconds(4);
        var started = DateTime.UtcNow;
        var read = await store.GetAsync(default);                                                                    // within the 60 s allowance: the held snapshot, flagged stale
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3.5));
        Assert.True(read.Stale);
        Assert.Same(held, read.Snapshot);
    }
}
