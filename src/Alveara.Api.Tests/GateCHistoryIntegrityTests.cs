using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;
using Alveara.Api.Architecture.Treatment;
using Xunit;
using Xunit.Abstractions;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// GATE C, criterion C8 (evidence tooling, not a product story): "No finalized clinical history can be silently overwritten". For each finalized or signed record class the reviewer named (signed clinical notes and their
/// addenda, diagnoses and their history, odontogram history, finalized periodontal records and sessions, treatment-plan revisions and authoritative state history, completed-procedure records) this builds a
/// record through the real services with synthetic data, then attacks it THROUGH THE PERSISTENCE LAYER: a plain SQL UPDATE or DELETE on a separate connection, inside a transaction that is always rolled back
/// (so an attack that succeeds cannot leave damage behind or disturb the next attack). For every attack it records the exact statement, whether the database rejected it, the SQL error number and the object that
/// rejected it (<see cref="SqlException.Procedure"/>: the trigger), and a before/after checksum of the whole table proving nothing changed. It then proves the sanctioned path (an addendum, a correction, a withdrawal,
/// a new chart, a new revision, an amendment) works and leaves the original untouched. The records are written to <c>GATE_C_EVIDENCE_OUT</c> (default: the temporary folder) as JSON and Markdown.
///
/// The tooling changes no product control and weakens none: it only reads and then attempts writes that it rolls back.
/// </summary>
[Trait("Gate", "C")]
public class GateCHistoryIntegrityTests(ITestOutputHelper output) : CompletionTestBase
{
    private sealed record Attempt(string RecordClass, string Table, string Statement, bool Required, bool Rejected, int? SqlError, string? Mechanism, string? Message, bool TableUnchanged, bool RowsChangedInsideTransaction);

    private readonly List<Attempt> _attempts = [];
    private readonly List<string> _paths = [];

    // ---------- the attack harness ----------

    private async Task<string> ChecksumAsync(string table, SqlTransaction? tx = null, SqlConnection? on = null)
    {
        var owns = on is null;
        var c = on ?? new SqlConnection(Fixture.ConnectionString);
        try
        {
            if (owns) await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT CAST(COUNT(*) AS varchar(20)) + ':' + CAST(ISNULL(CHECKSUM_AGG(CHECKSUM(*)), 0) AS varchar(20)) FROM [{table}]";
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        finally { if (owns) await c.DisposeAsync(); }
    }

    /// <summary>The SET clause for an UPDATE of one column to a different value, by the column's type; the first existing column of <paramref name="candidates"/> is used.</summary>
    private async Task<(string Column, string Set)> UpdateTargetAsync(string table, params string[] candidates)
    {
        await using var c = new SqlConnection(Fixture.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COLUMN_NAME, DATA_TYPE, ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}'";
        var columns = new Dictionary<string, (string Type, int Length)>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) columns[r.GetString(0)] = (r.GetString(1), r.GetInt32(2));
        foreach (var name in candidates)
        {
            if (!columns.TryGetValue(name, out var col)) continue;
            return (name, col.Type switch
            {
                "nvarchar" or "varchar" => $"[{name}] = LEFT(N'overwritten by the Gate C attempt', {(col.Length is -1 or 0 ? 4000 : Math.Min(col.Length, 33))})",
                "bit" => $"[{name}] = 1 - [{name}]",
                "datetimeoffset" or "datetime2" => $"[{name}] = DATEADD(day, 1, [{name}])",
                _ => $"[{name}] = [{name}] + 1",
            });
        }
        // none of the preferred columns exists: any ordinary data column will do for an attack (never a key, a link, a hash or the row version)
        foreach (var (name, col) in columns)
        {
            if (name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Key", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Hash", StringComparison.OrdinalIgnoreCase) || name == "RowVersion") continue;
            if (col.Type is not ("nvarchar" or "varchar" or "int" or "smallint" or "tinyint" or "decimal" or "datetimeoffset" or "datetime2")) continue;
            return (name, col.Type switch
            {
                "nvarchar" or "varchar" => $"[{name}] = LEFT(N'overwritten by the Gate C attempt', {(col.Length is -1 or 0 ? 4000 : Math.Min(col.Length, 33))})",
                "datetimeoffset" or "datetime2" => $"[{name}] = DATEADD(day, 1, [{name}])",
                _ => $"[{name}] = [{name}] + 1",
            });
        }
        throw new InvalidOperationException($"{table}: no column can be attacked (found: {string.Join(", ", columns.Keys)})");
    }

    private async Task AttackAsync(string recordClass, string table, string statement, bool required = true)
    {
        var before = await ChecksumAsync(table);
        await using var c = new SqlConnection(Fixture.ConnectionString);
        await c.OpenAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        SqlException? error = null;
        var affected = 0;
        string? inside = null;
        try
        {
            await using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = statement;
            affected = await cmd.ExecuteNonQueryAsync();
            inside = await ChecksumAsync(table, tx, c);
        }
        catch (SqlException e) { error = e; }
        try { await tx.RollbackAsync(); } catch (InvalidOperationException) { /* the server already ended the transaction */ }
        var after = await ChecksumAsync(table);
        var attempt = new Attempt(recordClass, table, statement, required, error is not null, error?.Number, error is null ? null : (string.IsNullOrEmpty(error.Procedure) ? $"(no trigger: SQL error {error.Number})" : error.Procedure),
            error?.Message, after == before, error is null && (affected > 0 || inside != before));
        _attempts.Add(attempt);
        output.WriteLine($"{recordClass,-34} {table,-30} {(attempt.Rejected ? "REJECTED " + attempt.SqlError + " by " + attempt.Mechanism : "NOT REJECTED (rows affected " + affected + ")"),-70} {statement}");
    }

    private async Task UpdateAttackAsync(string recordClass, string table, params string[] candidates) => await UpdateAttackAsync(recordClass, table, true, candidates);

    private async Task UpdateAttackAsync(string recordClass, string table, bool required, string[] candidates)
    {
        var (_, set) = await UpdateTargetAsync(table, candidates);
        var column = set[1..set.IndexOf(']')];
        await AttackAsync(recordClass, table, $"UPDATE [{table}] SET {set} WHERE [{column}] IS NOT NULL", required);
    }

    private Task DeleteAttackAsync(string recordClass, string table) => AttackAsync(recordClass, table, $"DELETE FROM [{table}]");

    private async Task<int> CountAsync(string table)
    {
        await using var c = new SqlConnection(Fixture.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    // ---------- the record classes ----------

    private Task<EncounterDetail> EncounterCall(Func<EncounterService, Task<EncounterDetail>> a) => WithDb(db => a(new EncounterService(db, Clock)));

    private async Task<EncounterDetail> CompleteDraftEncounterAsync()
    {
        var e = (await WithDb(db => new EncounterService(db, Clock).StartAsync(Ann, null, null, $"gc-{Guid.NewGuid():N}", S.Actor, default))).Detail;
        foreach (var kind in new[] { EncounterEntryKinds.MedicalHistory, EncounterEntryKinds.DentalHistory, EncounterEntryKinds.Allergy })
        {
            var current = e;
            e = await EncounterCall(s => s.AddEntryAsync(current.Id, kind, new EntryFields($"Synthetic {kind}", null, null, null, null, null), current.RowVersion, S.Actor, default));
        }
        var now = e;
        return await EncounterCall(s => s.MarkSectionNoneReportedAsync(now.Id, EncounterEntryKinds.Medication, now.RowVersion, S.Actor, default));
    }

    [Fact]
    public async Task C8_signed_notes_and_their_addenda()
    {
        var e = await CompleteDraftEncounterAsync();
        e = await WithDb(db => new EncounterNoteService(db, Clock).SaveNoteAsync(e.Id, NoteSections.Subjective, "Synthetic subjective note", e.RowVersion, S.Actor, default));
        var typed = e;
        e = await WithDb(db => new EncounterSigningService(db, Clock).SignAsync(typed.Id, typed.RowVersion, S.Actor, default));

        // signed, not yet finalized: the service refuses a change; what the database says is recorded, not assumed
        var signed = e;
        var refused = await Assert.ThrowsAsync<ClinicalException>(() => WithDb(db => new EncounterNoteService(db, Clock).SaveNoteAsync(signed.Id, NoteSections.Subjective, "overwrite while signed", signed.RowVersion, S.Actor, default)));
        _paths.Add($"signed note: a change through the service is refused ({refused.Code}, {refused.StatusCode}): {refused.Message}");
        await UpdateAttackAsync("signed note (signed, not finalized): observed, not required", "EncounterNotes", false, ["Body", "Text", "Content", "Note"]);

        var finalized = await EncounterCall(s => s.FinalizeAsync(signed.Id, signed.RowVersion, S.Actor, default));
        var noteBefore = await ChecksumAsync("EncounterNotes");
        var addendum = await WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(finalized.Id, "Synthetic addendum", $"gc-{Guid.NewGuid():N}", S.Actor, default));
        Assert.True(addendum.Created);
        Assert.Equal(noteBefore, await ChecksumAsync("EncounterNotes"));
        Assert.Equal(1, await CountAsync("EncounterAddenda"));

        await UpdateAttackAsync("finalized encounter note", "EncounterNotes", "Body", "Text", "Content", "Note");
        await DeleteAttackAsync("finalized encounter note", "EncounterNotes");
        await UpdateAttackAsync("finalized encounter", "Encounters", "EncounterAtUtc", "Status");
        await DeleteAttackAsync("finalized encounter", "Encounters");
        await UpdateAttackAsync("finalized encounter entries", "EncounterEntries", "Name");
        await DeleteAttackAsync("finalized encounter entries", "EncounterEntries");
        await UpdateAttackAsync("encounter history", "EncounterEvents", "ChangeType", "Details", "Detail", "EventType");
        await DeleteAttackAsync("encounter history", "EncounterEvents");
        await UpdateAttackAsync("encounter addendum", "EncounterAddenda", "Text", "Body", "Note");
        await DeleteAttackAsync("encounter addendum", "EncounterAddenda");

        var refusedAfter = await Assert.ThrowsAsync<ClinicalException>(() => WithDb(db => new EncounterNoteService(db, Clock).SaveNoteAsync(finalized.Id, NoteSections.Subjective, "overwrite after finalize", finalized.RowVersion, S.Actor, default)));
        _paths.Add($"finalized note: a change through the service is refused ({refusedAfter.Code}); the sanctioned path is an addendum: it was added (1 addendum row) and the note rows were byte for byte unchanged by it");
        AssertAllRequiredRejected();
        WriteEvidence("c8-signed-notes-and-addenda");
    }

    [Fact]
    public async Task C8_diagnoses_and_odontogram_history()
    {
        var recorded = await WithDb(db => new DiagnosisService(db, Clock).RecordAsync(Ann, Guid.NewGuid().ToString("N"), new DiagnosisInput(AnnEncounter, "Synthetic diagnosis", null, null, null), S.Actor, default));
        var diagnosis = recorded.Id;
        var view = await WithDb(db => new DiagnosisService(db, Clock).CorrectAsync(diagnosis, recorded.RowVersion, new DiagnosisCorrection("Synthetic corrected label", null, null, null, false), "Synthetic correction", S.Actor, default));
        _paths.Add($"diagnosis: a correction with a reason is the sanctioned path (history now holds {await CountAsync("DiagnosisVersions")} versions)");
        var finding = await FindingAsync("16", "O");
        await LinkAsync(diagnosis, finding);
        string version;
        await using (var db = Fixture.CreateContext()) version = Convert.ToBase64String(await db.ToothFindings.AsNoTracking().Where(f => f.Id == finding).Select(f => f.RowVersion).SingleAsync());
        await WithDb(db => new OdontogramService(db, Clock).ChangeStateAsync(finding, FindingStates.Completed, version, S.Actor, default));

        await DeleteAttackAsync("diagnosis", "Diagnoses");
        await AttackAsync("diagnosis", "Diagnoses", $"UPDATE [Diagnoses] SET [PatientId] = '{Bo}'");
        await UpdateAttackAsync("diagnosis history", "DiagnosisVersions", "Label");
        await DeleteAttackAsync("diagnosis history", "DiagnosisVersions");
        await UpdateAttackAsync("diagnosis link", "DiagnosisLinks", "Reference", "LinkType", "TargetType");
        await DeleteAttackAsync("diagnosis link", "DiagnosisLinks");

        var versionsBefore = await CountAsync("ToothFindingVersions");
        string current;
        await using (var db2 = Fixture.CreateContext()) current = Convert.ToBase64String(await db2.ToothFindings.AsNoTracking().Where(f => f.Id == finding).Select(f => f.RowVersion).SingleAsync());
        await WithDb(db => new OdontogramService(db, Clock).WithdrawAsync(finding, "Synthetic withdrawal", current, S.Actor, default));
        Assert.Equal(versionsBefore + 1, await CountAsync("ToothFindingVersions"));
        _paths.Add("odontogram: a wrong finding is withdrawn with a reason, which adds a history version; the finding and its earlier versions stay");
        await DeleteAttackAsync("odontogram finding", "ToothFindings");
        await UpdateAttackAsync("odontogram history", "ToothFindingVersions", "State", "Condition", "Reason");
        await DeleteAttackAsync("odontogram history", "ToothFindingVersions");
        await UpdateAttackAsync("odontogram link", "ToothFindingLinks", "Reference", "LinkType");
        await DeleteAttackAsync("odontogram link", "ToothFindingLinks");
        Assert.NotNull(view);
        AssertAllRequiredRejected();
        WriteEvidence("c8-diagnoses-and-odontogram");
    }

    [Fact]
    public async Task C8_finalized_periodontal_records_and_sessions()
    {
        PerioReadingInput[] Tooth(string tooth) => [.. PerioRules.Sites.Select(s => new PerioReadingInput(tooth, s, 3, 1, false, null, null))];
        var session = await WithDb(db => new PerioSessionService(db, Clock).StartAsync(Ann, S.Actor, default));
        session = await WithDb(db => new PerioSessionService(db, Clock).SaveEntriesAsync(session.Id, session.RowVersion, new PerioEntryBatch(Tooth("16"), [new PerioToothInput("16", 1, null, false)], null, null), S.Actor, default));
        var open = session;
        await WithDb(db => new PerioSessionService(db, Clock).FinalizeAsync(open.Id, open.RowVersion, S.Actor, default));
        var second = await WithDb(db => new PerioSessionService(db, Clock).StartAsync(Ann, S.Actor, default));
        second = await WithDb(db => new PerioSessionService(db, Clock).SaveEntriesAsync(second.Id, second.RowVersion, new PerioEntryBatch(Tooth("16"), [new PerioToothInput("16", 1, null, false)], null, null), S.Actor, default));
        var secondOpen = second;
        await WithDb(db => new PerioSessionService(db, Clock).FinalizeAsync(secondOpen.Id, secondOpen.RowVersion, S.Actor, default));
        Assert.Equal(2, await CountAsync("PerioExams"));
        _paths.Add("perio: a finalized chart is never edited; the sanctioned path is a new chart: a second session was finalized as a second exam (2 exams held)");

        await UpdateAttackAsync("finalized perio chart", "PerioExams", "RecordedAtUtc", "ReadingCount");
        await DeleteAttackAsync("finalized perio chart", "PerioExams");
        await UpdateAttackAsync("finalized perio readings", "PerioReadings", "ProbingDepthMm", "PocketDepthMm", "DepthMm", "ProbingDepth", "Depth");
        await DeleteAttackAsync("finalized perio readings", "PerioReadings");
        await UpdateAttackAsync("finalized perio tooth records", "PerioToothRecords", "Mobility", "Furcation");
        await DeleteAttackAsync("finalized perio tooth records", "PerioToothRecords");
        await UpdateAttackAsync("finalized perio session", "PerioSessions", "UpdatedAtUtc", "Status");
        await DeleteAttackAsync("finalized perio session", "PerioSessions");
        await UpdateAttackAsync("finalized perio session readings", "PerioSessionReadings", "ProbingDepthMm", "PocketDepthMm", "DepthMm", "ProbingDepth", "Depth");
        await UpdateAttackAsync("finalized perio session teeth", "PerioSessionTeeth", "Mobility", "Furcation");
        AssertAllRequiredRejected();
        WriteEvidence("c8-finalized-perio");
    }

    [Fact]
    public async Task C8_treatment_plan_revisions_state_history_and_completed_procedures()
    {
        var plan = await AcceptedPlanAsync(2);
        plan = await Svc(s => s.ReviseAsync(plan.Id, new ReviseInput("Synthetic revision", "Synthetic rationale", "Synthetic explanation", [new PlanPhaseInput(1, "First visit", "Today", null)], [new PlanPlacementInput(plan.Items[0].Id, 1)]), plan.RowVersion, S.Actor, default));
        _paths.Add($"treatment plan: a revision is the sanctioned path (the plan is now at revision {plan.CurrentRevision}; earlier revisions are kept)");
        var request = RequestFor(plan, [0], AnnEncounter, (await ProviderAsync("Dr. Gate")).Id, "gate-c-1");
        await Completion(s => s.CompleteAsync(plan.Id, request, S.Actor, default));

        await AttackAsync("treatment plan identity", "TreatmentPlans", $"UPDATE [TreatmentPlans] SET [PatientId] = '{Bo}'");
        await DeleteAttackAsync("treatment plan", "TreatmentPlans");
        await UpdateAttackAsync("treatment plan revision", "TreatmentPlanRevisions", "ClinicalRationale", "PatientExplanation", "Reason");
        await DeleteAttackAsync("treatment plan revision", "TreatmentPlanRevisions");
        await UpdateAttackAsync("treatment plan revision phases", "TreatmentPlanRevisionPhases", "Name", "PlannedVisit");
        await DeleteAttackAsync("treatment plan revision phases", "TreatmentPlanRevisionPhases");
        await UpdateAttackAsync("treatment plan revision placements", "TreatmentPlanRevisionItems", "PhaseNumber");
        await DeleteAttackAsync("treatment plan revision placements", "TreatmentPlanRevisionItems");
        await UpdateAttackAsync("treatment plan history", "TreatmentPlanEvents", "Title", "ChangeType", "Reason");
        await DeleteAttackAsync("treatment plan history", "TreatmentPlanEvents");
        await UpdateAttackAsync("treatment plan items (the copied fee)", "TreatmentPlanItems", "Fee", "FeeAmount", "FeeEstimate");
        await DeleteAttackAsync("treatment plan items", "TreatmentPlanItems");
        await UpdateAttackAsync("treatment plan state history", "TreatmentPlanItemStates", "Reason", "AnsweredByName", "CreatedAtUtc");
        await DeleteAttackAsync("treatment plan state history", "TreatmentPlanItemStates");

        await UpdateAttackAsync("completed procedure", "CompletedProcedures", "ToothKey", "Surface", "ChartOutcome");
        await DeleteAttackAsync("completed procedure", "CompletedProcedures");
        await UpdateAttackAsync("completion set", "CompletionSets", "IdempotencyKey", "RequestHash");
        await DeleteAttackAsync("completion set", "CompletionSets");
        await AttackAsync("completed plan item (withdraw it)", "TreatmentPlanItems",
            $"UPDATE [TreatmentPlanItems] SET [WithdrawnAtUtc] = SYSDATETIMEOFFSET(), [WithdrawnByUserId] = (SELECT TOP 1 [Id] FROM [UserAccounts]), [WithdrawnReason] = N'overwritten by the Gate C attempt' WHERE [Id] IN (SELECT [PlanItemId] FROM [CompletedProcedures])");
        var completedBefore = await ChecksumAsync("CompletedProcedures");
        var completed = await WithDb(db => db.CompletedProcedures.AsNoTracking().Select(x => x.Id).SingleAsync());
        var other = await ProviderAsync("Dr. Amend");
        var amended = await Completion(s => s.AmendAsync(completed, new AmendCompletedProcedureRequest("gate-c-amend-1", 0, other.Id, null, null, "Recorded under the wrong provider"), S.Actor, default));
        Assert.NotNull(amended);
        Assert.Equal(completedBefore, await ChecksumAsync("CompletedProcedures"));
        _paths.Add("completed procedure: a correction is an amendment, appended and never an edit; the completed-procedure rows were byte for byte unchanged by it");
        AssertAllRequiredRejected();
        WriteEvidence("c8-plans-and-completion");
    }

    // ---------- the evidence ----------

    /// <summary>Every attempt that must be rejected was rejected by the database, and every attempted table is exactly as it was.</summary>
    private void AssertAllRequiredRejected()
    {
        var failures = _attempts.Where(a => a.Required && !a.Rejected).Select(a => $"{a.RecordClass}: {a.Statement}").ToList();
        Assert.True(failures.Count == 0, "not rejected by the database: " + string.Join("; ", failures));
        Assert.All(_attempts, a => Assert.True(a.TableUnchanged, $"{a.RecordClass}: {a.Statement} left its table changed after the rollback"));
    }

    private void WriteEvidence(string name)
    {
        var dir = Environment.GetEnvironmentVariable("GATE_C_EVIDENCE_OUT") ?? Path.Combine(Path.GetTempPath(), "gate-c-evidence");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), JsonSerializer.Serialize(new { attempts = _attempts, sanctionedPaths = _paths }, new JsonSerializerOptions { WriteIndented = true }));
        var md = new StringBuilder("| Record class | Table | Statement | Result | SQL error | Rejected by | Table unchanged |\n|---|---|---|---|---|---|---|\n");
        foreach (var a in _attempts)
            md.AppendLine($"| {a.RecordClass} | {a.Table} | `{a.Statement.Replace("|", "\\|")}` | {(a.Rejected ? "REJECTED" : "NOT REJECTED")} | {a.SqlError} | {a.Mechanism} | {a.TableUnchanged} |");
        File.WriteAllText(Path.Combine(dir, $"{name}.md"), md.ToString());
    }
}
