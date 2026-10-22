namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N016 R02. A restore of a clone takes about 0.3 s and the tests are strictly serial, so with one clone restored per test the restore time adds to the run. This pool keeps a few clones restored AHEAD, in the
/// background, while the current test runs; a fixture then takes a clone that is already there. Tests still run one after another: only the preparation of databases overlaps. Nothing here changes what a clone is:
/// the same restore of the same backup, under its own name (<c>AlveraPool_*</c>), claimed by exactly one fixture.
///
/// Rules: a clone is handed out once; the pool refills as it is drawn from, to at most <c>depth</c> clones prepared or being prepared; a background restore that failed is seen only by the fixture that claims it,
/// which cleans up and falls back; clones nobody claimed are dropped when the pool is disposed (the run's pool is disposed when the process exits). A killed process can leave <c>AlveraPool_*</c> databases;
/// they are reported by the next run's artifact counts and never deleted automatically.
/// </summary>
internal sealed class DatabasePrefetcher : IDisposable
{
    /// <summary>A clone that is ready or being prepared: its name and the task that completes when it exists (or fails).</summary>
    internal sealed record Entry(string Name, Task Ready);

    public const string NamePrefix = "AlveraPool_";
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Queue<Entry> _queue = new();
    private readonly Func<string, Task> _produce;
    private readonly Func<string, Task> _drop;
    private readonly Action<string, string, object> _event;
    private readonly int _depth;
    private bool _started, _stopped;
    private int _hits, _misses, _waits;

    public DatabasePrefetcher(int depth, Func<string, Task> produce, Func<string, Task> drop, Action<string, string, object> raise)
    {
        _depth = Math.Max(0, depth);
        _produce = produce;
        _drop = drop;
        _event = raise;
    }

    public int Hits => Volatile.Read(ref _hits);
    public int Misses => Volatile.Read(ref _misses);
    /// <summary>How many claims found their clone still being restored and had to wait for it.</summary>
    public int Waits => Volatile.Read(ref _waits);

    /// <summary>The clones restored and not yet claimed.</summary>
    public int Ready
    {
        get { lock (_gate) return _queue.Count(e => e.Ready.IsCompletedSuccessfully); }
    }

    /// <summary>Begins preparing clones; called once the template exists.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _stopped || _depth == 0) return;
            _started = true;
            Refill();
        }
    }

    /// <summary>The oldest prepared clone, or null when there is none to give (not started, depth 0, or stopped). A claim refills the pool.</summary>
    public Entry? TryClaim()
    {
        lock (_gate)
        {
            if (_depth == 0 || _stopped) return null;
            if (!_started || _queue.Count == 0)
            {
                Interlocked.Increment(ref _misses);
                return null;
            }
            var entry = _queue.Dequeue();
            Interlocked.Increment(ref _hits);
            if (!entry.Ready.IsCompleted) Interlocked.Increment(ref _waits);
            Refill();
            return entry;
        }
    }

    /// <summary>Drops every prepared clone that nobody claimed and prepares new ones from the current state of the template (used when the backup changed).</summary>
    public async Task DiscardAsync()
    {
        Entry[] taken;
        lock (_gate) { taken = _queue.ToArray(); _queue.Clear(); }
        await DropAllAsync(taken);
    }

    /// <summary>Refills the pool up to its depth (after <see cref="DiscardAsync"/>).</summary>
    public void Restart()
    {
        lock (_gate)
        {
            if (_started && !_stopped) Refill();
        }
    }

    private void Refill()
    {
        while (!_stopped && _queue.Count < _depth)
        {
            var name = NamePrefix + Guid.NewGuid().ToString("N");
            _queue.Enqueue(new Entry(name, Task.Run(() => _produce(name))));
        }
    }

    public void Dispose()
    {
        Entry[] left;
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            left = _queue.ToArray();
            _queue.Clear();
        }
        DropAllAsync(left).GetAwaiter().GetResult();
    }

    private async Task DropAllAsync(Entry[] entries)
    {
        var dropped = 0;
        foreach (var e in entries)
        {
            try { await e.Ready.WaitAsync(DisposeWait); }
            catch (Exception ex) { _event("template_prefetch_restore_failed", "warn", new { error_class = ex.GetType().Name }); }   // the clone never became ready; the drop below removes whatever it left
            try { await _drop(e.Name); dropped++; }
            catch (Exception ex) { _event("template_cleanup_failed", "warn", new { error_class = ex.GetType().Name, database = e.Name }); }
        }
        if (entries.Length > 0) _event("template_prefetch_dropped", "info", new { dropped, unclaimed = entries.Length });
    }
}
