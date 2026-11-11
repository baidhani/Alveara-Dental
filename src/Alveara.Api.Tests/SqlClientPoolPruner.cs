using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N019 R04: forces the REAL SqlClient pool-pruning condition. SqlClient runs one internal timer (the connection factory's pruning timer, callback <c>PruneConnectionPoolGroups</c>) that shuts down pools
/// which hold no connection; the TESTSPEED-P2 investigation showed that a tick landing while a caller acquires from a still-empty pool makes that caller fail with "Timeout expired ... obtaining a connection
/// from the pool". The timer's own schedule is minutes, so tests call the SAME callback on a fast cadence instead of waiting. Both the timer field and the callback are found by NAME in the SqlClient
/// assembly (they are internal); when a SqlClient upgrade renames them, <see cref="Describe"/> says so and the mechanism test fails loudly instead of the proof passing vacuously.
/// </summary>
internal static class SqlClientPoolPruner
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Lazy<(object? Instance, MethodInfo? Callback, string Description)> Found = new(Discover);

    public static string Describe() => Found.Value.Description;

    public static bool IsAvailable => Found.Value.Callback is not null;

    /// <summary>Runs one pruning tick now (the same callback SqlClient's own timer runs). Throws if the mechanism was not found.</summary>
    public static void Tick()
    {
        var (instance, callback, description) = Found.Value;
        if (callback is null) throw new InvalidOperationException("The SqlClient pruning callback was not found: " + description);
        callback.Invoke(callback.IsStatic ? null : instance, [null]);
    }

    /// <summary>Starts a background loop that ticks every <paramref name="interval"/> until the returned handle is disposed.</summary>
    public static IAsyncDisposable StartTicking(TimeSpan interval) => new Ticker(interval);

    private sealed class Ticker : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public Ticker(TimeSpan interval)
        {
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    Tick();
                    try { await Task.Delay(interval, _stop.Token); } catch (OperationCanceledException) { return; }
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _loop;
            _stop.Dispose();
        }
    }

    private static (object? Instance, MethodInfo? Callback, string Description) Discover()
    {
        var assembly = typeof(SqlConnection).Assembly;
        var notes = new List<string> { $"assembly {assembly.GetName().Name} {assembly.GetName().Version}" };
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = [.. ex.Types.Where(t => t is not null).Select(t => t!)]; notes.Add("some types could not be loaded"); }

        foreach (var type in types)
        {
            var timerField = type.GetFields(All | BindingFlags.DeclaredOnly).FirstOrDefault(f => f.Name == "_pruningTimer");
            if (timerField is null) continue;
            var callback = type.GetMethods(All | BindingFlags.DeclaredOnly).FirstOrDefault(m => m.Name == "PruneConnectionPoolGroups" && m.GetParameters().Length == 1);
            notes.Add($"type {type.FullName} has _pruningTimer; callback {(callback is null ? "NOT FOUND" : "found")}");
            if (callback is null) continue;

            object? instance = null;
            if (!callback.IsStatic)
            {
                var singleton = type.GetFields(All).FirstOrDefault(f => f.IsStatic && f.FieldType == type) is { } sf ? sf.GetValue(null)
                    : type.GetProperties(All).FirstOrDefault(p => p.GetMethod?.IsStatic == true && p.PropertyType == type)?.GetValue(null);
                instance = singleton;
                if (instance is null) { notes.Add("no static instance of the factory was found"); continue; }
            }
            notes.Add("mechanism ready");
            return (instance, callback, string.Join("; ", notes));
        }

        notes.Add("no type with a _pruningTimer field and a PruneConnectionPoolGroups(object) callback");
        return (null, null, string.Join("; ", notes));
    }
}
