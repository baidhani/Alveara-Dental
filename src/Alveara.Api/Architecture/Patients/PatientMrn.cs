using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Alveara.Api.Architecture.Patients;

/// <summary>
/// ALV-N025 (PD-21, brief section 5): the patient's Medical Record Number - a lookup reference, never identity (nothing authenticates with it).
/// - The value is UTC whole seconds since 0001-01-01 (<c>Ticks / TicksPerSecond</c>): 11 digits for the coming centuries.
/// - A new patient takes the current second; while a patient has that number it steps one second forward. Never a wait, never a suffix.
/// - A concurrent registration that took the same number is caught by the unique index (<see cref="IndexName"/>): one is added and the save tried again,
///   at most <see cref="MaxAttempts"/> times, then <c>503 mrn_busy</c>.
/// - Immutable (guard 51298 in the database, and EF refuses to send a changed value) and never reused: patients are never deleted, so the unique index keeps
///   every number reserved, a merged-away record's included; and (MRN rollback addendum, ADDENDUM-R01) every number given is also kept in the append-only
///   <c>PatientMrnArchive</c> (written by the database on insert, outside the EF model), which the allocator treats as taken too, so a rollback and re-apply
///   can never free a number for someone else.
/// Assignment happens in one place, the context's save (<see cref="Data.AlveraDbContext"/>), so every path that adds a patient - the front desk, the
/// portability import, the bulk import - gets a number the same way.
/// </summary>
public static class PatientMrn
{
    public const string IndexName = "IX_Patients_Mrn";
    public const string ArchiveIndexName = "UX_PatientMrnArchive_Mrn";
    public const int MaxAttempts = 60;
    public const int Digits = 11;

    /// <summary>How far past the current second the save looks for numbers already taken (a burst larger than this is still safe: the index and the retry settle it).</summary>
    private const int LookAhead = 1000;

    /// <summary>The MRN form of an instant: UTC whole seconds since 0001-01-01.</summary>
    public static long SecondsOf(DateTimeOffset instant) => instant.UtcTicks / TimeSpan.TicksPerSecond;

    /// <summary>True when the text is exactly an MRN: 11 ASCII digits (the search's exact lookup, tried before the phone rule).</summary>
    public static bool IsMrnText(string? text) => text is { Length: Digits } && text.All(char.IsAsciiDigit);

    /// <summary>The patients this save adds that have no number yet (a caller that set one keeps it).</summary>
    internal static List<Patient> Unassigned(ChangeTracker tracker) =>
        tracker.Entries<Patient>().Where(e => e.State == EntityState.Added && e.Entity.Mrn == 0).Select(e => e.Entity).ToList();

    /// <summary>The numbers already taken from <paramref name="start"/> on, as far as the look-ahead reaches: on a patient, or in the archive (never handed out again).</summary>
    internal static async Task<HashSet<long>> TakenAsync(Data.AlveraDbContext db, long start, int count, CancellationToken ct)
    {
        var end = start + count + LookAhead;
        var taken = (await db.Patients.AsNoTracking().Where(p => p.Mrn >= start && p.Mrn < end).Select(p => p.Mrn).ToListAsync(ct)).ToHashSet();
        taken.UnionWith(await db.Database.SqlQuery<long>($"SELECT [Mrn] AS [Value] FROM [PatientMrnArchive] WHERE [Mrn] >= {start} AND [Mrn] < {end}").ToListAsync(ct));
        return taken;
    }

    internal static HashSet<long> Taken(Data.AlveraDbContext db, long start, int count)
    {
        var end = start + count + LookAhead;
        var taken = db.Patients.AsNoTracking().Where(p => p.Mrn >= start && p.Mrn < end).Select(p => p.Mrn).ToHashSet();
        taken.UnionWith(db.Database.SqlQuery<long>($"SELECT [Mrn] AS [Value] FROM [PatientMrnArchive] WHERE [Mrn] >= {start} AND [Mrn] < {end}").ToList());
        return taken;
    }

    /// <summary>Gives each new patient the first free second from <paramref name="start"/> on, in order, never two the same.</summary>
    internal static void Assign(IEnumerable<Patient> fresh, long start, HashSet<long> taken)
    {
        var next = start;
        foreach (var patient in fresh)
        {
            while (taken.Contains(next)) next++;
            patient.Mrn = next;
            taken.Add(next);
            next++;
        }
    }

    /// <summary>A concurrent registration took one of the numbers: add one to each and try again (the order and distinctness are kept).</summary>
    internal static void StepForward(IEnumerable<Patient> fresh)
    {
        foreach (var patient in fresh) patient.Mrn += 1;
    }

    /// <summary>
    /// The unique-index violation of an MRN index - the patients' or the archive's (a number only in the archive, raised by the archiving trigger) - and only those
    /// (the duplicate and registration-key indexes keep their own handling).
    /// </summary>
    internal static bool IsCollision(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql
        && (sql.Message.Contains(IndexName, StringComparison.Ordinal) || sql.Message.Contains(ArchiveIndexName, StringComparison.Ordinal));

    internal static PatientException Busy() =>
        new("mrn_busy", "A patient number could not be given right now because many patients are being registered at once. Try again in a moment.", 503);
}
