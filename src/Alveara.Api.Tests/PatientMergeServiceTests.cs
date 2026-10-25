using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Recall;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-017, the merge service against a real SQL Server and a real chart: Ann (to be retired) and Bo (to be kept) each have a diagnosis, a treatment plan with its items and history, and a safety alert. A merge moves every
/// row that held Ann's patient id to Bo, and the test proves it for every column the registry lists (nothing lost, nothing duplicated, nothing left behind), leaves Ann in place and merged, writes the lineage, the history and
/// two audit entries with the user and the time, answers a repeat with the merge already made, and changes nothing at all when it is refused or fails part way.
/// </summary>
public class PatientMergeServiceTests : TreatmentPlanTestBase
{
    private async Task<T> WithService<T>(Func<PatientMergeService, Task<T>> action) => await WithDb(db => action(new PatientMergeService(db, Clock)));

    private async Task GiveChartAsync(Guid patient, string label)
    {
        var diagnosis = await DiagnosisAsync(patient, label);
        var procedure = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        await CreateAsync([Item(diagnosis, procedure.Summary.Id)], title: $"{label} plan", patient: patient);
    }

    private async Task<MergeResult> MergeAsync(Guid source, Guid survivor, string? key = null, Dictionary<string, string>? choices = null, string reason = "Same person registered twice")
    {
        var preview = await WithService(s => s.PreviewAsync(source, survivor, default));
        return await WithService(s => s.MergeAsync(S.Actor, new MergeRequest(source, survivor, reason, preview.SourceRowVersion, preview.SurvivorRowVersion, choices), key ?? Guid.NewGuid().ToString("N"), default));
    }

    private async Task<PatientException> RefusedAsync(Func<Task> action) => await Assert.ThrowsAsync<PatientException>(action);

    private async Task<Dictionary<string, int>> CountsAsync(Guid patient)
    {
        await using var db = Fixture.CreateContext();
        var counts = new Dictionary<string, int>();
        foreach (var c in PatientOwnedData.All)
            counts[$"{c.Table}.{c.Column}"] = await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM {PatientMergeCatalog.Q(c.Table)} WHERE {PatientMergeCatalog.Q(c.Column)} = @p", new SqlParameter("@p", patient)).SingleAsync();
        return counts;
    }

    private async Task<int> ScalarAsync(string sql, params SqlParameter[] parameters) { await using var db = Fixture.CreateContext(); return await db.Database.SqlQueryRaw<int>(sql, parameters).SingleAsync(); }

    private async Task SetAsync(Guid patient, string column, string? value)
    {
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync($"UPDATE [Patients] SET {PatientMergeCatalog.Q(column)} = @v WHERE [Id] = @p", new SqlParameter("@v", (object?)value ?? DBNull.Value), new SqlParameter("@p", patient));
    }

    // ---------- the happy path ----------

    [Fact]
    public async Task A_merge_moves_every_row_that_held_the_retired_patients_id_to_the_survivor_and_loses_nothing()
    {
        await GiveChartAsync(Ann, "Gingivitis"); await GiveChartAsync(Bo, "Periodontitis");
        await AddAlertAsync("Latex allergy", patient: Ann); await AddAlertAsync("Penicillin allergy", patient: Bo);
        var before = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        Assert.True(before.Ann["TreatmentPlans.PatientId"] > 0 && before.Ann["Diagnoses.PatientId"] > 0 && before.Ann["SafetyAlerts.PatientId"] > 0);       // there is something to move

        var result = await MergeAsync(Ann, Bo);

        var after = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        foreach (var column in PatientOwnedData.All.Where(c => c.Table != "PatientHistory"))
        {
            var k = $"{column.Table}.{column.Column}";
            Assert.True(after.Ann[k] == 0, $"{k}: {after.Ann[k]} rows are still the retired patient's");
            Assert.True(after.Bo[k] == before.Ann[k] + before.Bo[k], $"{k}: the survivor has {after.Bo[k]}, expected {before.Ann[k]} + {before.Bo[k]}");
        }
        Assert.Equal(before.Ann["PatientHistory.PatientId"] + before.Bo["PatientHistory.PatientId"] + 0, after.Bo["PatientHistory.PatientId"] - await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientHistory] WHERE [PatientId] = @p AND [ChangedByUserId] = @a AND [ChangeType] = 'Updated'", new SqlParameter("@p", Bo), new SqlParameter("@a", S.Actor)));
        Assert.Equal(result.Moves.Sum(m => m.Rows), PatientOwnedData.All.Where(c => c.Table != "PatientHistory").Sum(c => before.Ann[$"{c.Table}.{c.Column}"]) + before.Ann["PatientHistory.PatientId"]);
        Assert.False(result.Replayed);
    }

    [Fact]
    public async Task A_record_that_was_merged_away_is_not_suggested_as_a_duplicate_again()
    {
        await SetAsync(Bo, "Phone", "(555) 010-0001"); await SetAsync(Ann, "Phone", "(555) 010-0001");
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [DateOfBirth] = (SELECT [DateOfBirth] FROM [Patients] WHERE [Id] = {0}), [LastName] = (SELECT [LastName] FROM [Patients] WHERE [Id] = {0}) WHERE [Id] = {1}", Ann, Bo);
        var before = await WithDb(db => new PatientDuplicateScanner(db).ScanAsync(default));
        Assert.Single(before.Pairs);
        await MergeAsync(Ann, Bo);
        var after = await WithDb(db => new PatientDuplicateScanner(db).ScanAsync(default));
        Assert.Empty(after.Pairs);
        Assert.Equal(0, after.PatientsConsidered);
    }

    [Fact]
    public async Task The_retired_patient_stays_in_place_merged_and_inactive_and_the_lineage_the_history_and_the_audit_say_who_and_when()
    {
        await GiveChartAsync(Ann, "Gingivitis");
        var result = await MergeAsync(Ann, Bo, reason: "Registered twice at the front desk");

        await using var db = Fixture.CreateContext();
        var ann = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann);
        Assert.False(ann.IsActive); Assert.Equal(result.MergedAtUtc, ann.MergedAtUtc);                                                  // in place, marked merged, never deleted
        Assert.True((await db.Patients.AsNoTracking().SingleAsync(p => p.Id == Bo)).MergedAtUtc is null);
        var lineage = await db.PatientMerges.AsNoTracking().SingleAsync();
        Assert.Equal((Ann, Bo, S.Actor, "Registered twice at the front desk"), (lineage.SourcePatientId, lineage.SurvivorPatientId, lineage.MergedByUserId, lineage.Reason));
        Assert.Equal(result.MergedAtUtc, lineage.MergedAtUtc);
        Assert.Equal(PatientOwnedData.All.Count, await db.PatientMergeMoves.CountAsync(m => m.MergeId == lineage.Id));                  // every column was looked at, zero too

        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "PatientMerged").ToListAsync();
        Assert.Equal(2, audit.Count);                                                                                                  // one for each record
        Assert.All(audit, a => { Assert.Equal(S.Actor, a.PerformedByUserAccountId); Assert.Equal(lineage.Id, a.CorrelationId); Assert.True(a.TimestampUtc > DateTimeOffset.UtcNow.AddMinutes(-5)); Assert.Equal("Registered twice at the front desk", a.Reason); });
        Assert.Equal(new[] { Ann, Bo }.Order().ToArray(), audit.Select(a => a.TargetUserAccountId).Order().ToArray());
        Assert.Contains(await db.PatientHistory.AsNoTracking().Where(h => h.PatientId == Ann).ToListAsync(), h => h.ChangeType == "Inactivated" && h.ChangedByUserId == S.Actor);
    }

    [Fact]
    public async Task A_preview_shows_what_a_merge_would_do_and_changes_nothing()
    {
        await GiveChartAsync(Ann, "Gingivitis");
        await SetAsync(Ann, "Phone", "(555) 010-0001"); await SetAsync(Bo, "Phone", "(555) 010-0002"); await SetAsync(Ann, "AddressLine2", "Apt 4"); await SetAsync(Bo, "AddressLine2", null);
        var before = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));

        var preview = await WithService(s => s.PreviewAsync(Ann, Bo, default));

        Assert.True(preview.CanMerge);
        Assert.Equal(new[] { "Phone" }, preview.Conflicts.Select(c => c.Field));
        Assert.Equal(("(555) 010-0002", "(555) 010-0001"), (preview.Conflicts[0].SurvivorValue, preview.Conflicts[0].SourceValue));
        Assert.Equal(new[] { "AddressLine2" }, preview.AutoFills.Select(f => f.Field));
        Assert.Equal(before.Ann["TreatmentPlans.PatientId"], preview.Moves.Single(m => m.Table == "TreatmentPlans" && m.Column == "PatientId").Rows);
        Assert.Empty(preview.LeftBehind);
        Assert.Equal(before.Ann, await CountsAsync(Ann)); Assert.Equal(before.Bo, await CountsAsync(Bo));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [MergedAtUtc] IS NOT NULL"));
    }

    // ---------- conflicts ----------

    [Fact]
    public async Task A_field_conflict_needs_an_answer_and_each_answer_is_applied_and_recorded_with_the_value_it_replaced()
    {
        await SetAsync(Ann, "Phone", "(555) 010-0001"); await SetAsync(Bo, "Phone", "(555) 010-0002");
        await SetAsync(Ann, "Email", "ann@example.org"); await SetAsync(Bo, "Email", "bo@example.org");
        await SetAsync(Ann, "AddressLine2", "Apt 4"); await SetAsync(Bo, "AddressLine2", null);                                         // filled in, not a conflict
        await SetAsync(Ann, "City", "Austin"); await SetAsync(Bo, "City", "AUSTIN");                                                    // the same apart from case, not a conflict

        var unanswered = await RefusedAsync(() => MergeAsync(Ann, Bo));
        Assert.Equal(("choice_required", 400), (unanswered.Code, unanswered.StatusCode));
        Assert.Equal(new[] { "Email", "Phone" }, unanswered.FieldErrors.Keys.Order().ToArray());
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));                                          // nothing was changed

        Assert.Equal("choice_invalid", (await RefusedAsync(() => MergeAsync(Ann, Bo, choices: new() { ["Phone"] = "Source", ["Email"] = "Survivor", ["LastName"] = "Source" }))).Code);
        Assert.Equal("choice_invalid", (await RefusedAsync(() => MergeAsync(Ann, Bo, choices: new() { ["Phone"] = "Both", ["Email"] = "Survivor" }))).Code);

        var result = await MergeAsync(Ann, Bo, choices: new() { ["Phone"] = "Source", ["Email"] = "Survivor" });

        await using var db = Fixture.CreateContext();
        var bo = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == Bo);
        Assert.Equal(("(555) 010-0001", "bo@example.org", "Apt 4", "AUSTIN"), (bo.Phone, bo.Email, bo.AddressLine2, bo.City));
        var fields = result.Resolutions.Where(r => r.Kind == "Field").ToDictionary(r => r.Subject);
        Assert.Equal(("TookSource", "(555) 010-0002"), (fields["Phone"].Choice, fields["Phone"].ReplacedValue));                       // what the survivor had is kept
        Assert.Equal(("KeptSurvivor", null), (fields["Email"].Choice, fields["Email"].ReplacedValue));
        Assert.Equal(("TookSource", null), (fields["AddressLine2"].Choice, fields["AddressLine2"].ReplacedValue));
        Assert.False(fields.ContainsKey("City"));
        Assert.Equal(3, await db.PatientMergeResolutions.CountAsync());
        Assert.Contains(await db.PatientHistory.AsNoTracking().Where(h => h.PatientId == Bo).ToListAsync(), h => h.FieldName == "Phone" && h.OldValue == "(555) 010-0002" && h.NewValue == "(555) 010-0001");
    }

    [Fact]
    public async Task A_record_the_survivor_already_has_its_one_of_stays_with_the_retired_patient_and_so_does_everything_that_depends_on_it()
    {
        await AddAlertAsync("Latex allergy", patient: Ann); await AddAlertAsync("Latex allergy", patient: Bo);                          // the same alert on both charts
        await AddAlertAsync("Penicillin allergy", patient: Ann);                                                                       // and one only the retired chart has
        await using (var db = Fixture.CreateContext())
        {
            db.PatientRecallPlans.AddRange(new PatientRecallPlan { PatientId = Ann, IntervalDays = 90, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor },
                                           new PatientRecallPlan { PatientId = Bo, IntervalDays = 180, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor });
            await db.SaveChangesAsync();
        }
        var versionsBefore = await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [SafetyAlertVersions]");
        var preview = await WithService(s => s.PreviewAsync(Ann, Bo, default));
        Assert.True(preview.CanMerge);
        Assert.Contains(preview.LeftBehind, l => l.Table == "PatientRecallPlans");
        Assert.Contains(preview.LeftBehind, l => l.Table == "SafetyAlerts");
        Assert.Contains(preview.LeftBehind, l => l.Table == "SafetyAlertVersions");                                                     // its history follows it

        var result = await MergeAsync(Ann, Bo);

        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientRecallPlans] WHERE [PatientId] = @p AND [IntervalDays] = 90", new SqlParameter("@p", Ann)));     // still the retired chart's
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientRecallPlans] WHERE [PatientId] = @p AND [IntervalDays] = 180", new SqlParameter("@p", Bo)));    // the survivor's own is untouched
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [SafetyAlerts] WHERE [PatientId] = @p AND [Title] = 'Latex allergy'", new SqlParameter("@p", Ann)));
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [SafetyAlerts] WHERE [PatientId] = @p", new SqlParameter("@p", Bo)));                                    // its own plus the one that could move
        Assert.Equal(versionsBefore, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [SafetyAlertVersions]"));                                                                 // nothing lost
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [SafetyAlertVersions] v JOIN [SafetyAlerts] a ON a.[Id] = v.[AlertId] WHERE v.[PatientId] <> a.[PatientId]"));   // and nothing disagrees
        var records = result.Resolutions.Where(r => r.Kind == "Record").ToList();
        Assert.All(records, r => Assert.Equal("LeftWithSource", r.Choice));
        Assert.Equal(records.Count, preview.LeftBehind.Count);
    }

    [Fact]
    public async Task A_merge_is_blocked_when_a_record_that_must_stay_points_at_one_that_would_move_and_nothing_is_changed()
    {
        var item = await RecordItemAsync("MedicalHistory", "Atrial fibrillation");                                                      // the retired chart's history item: it would move
        await AddAlertAsync("Latex allergy", patient: Ann, sourceItem: item.Id);                                                        // the retired chart's alert cites it, and the survivor has the same alert: that one has to stay
        await AddAlertAsync("Latex allergy", patient: Bo);
        var before = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));

        var preview = await WithService(s => s.PreviewAsync(Ann, Bo, default));
        Assert.False(preview.CanMerge);
        Assert.Contains(preview.Blockers, b => b.Code == "shared_record" && b.Message.Contains("SafetyAlerts") && b.Message.Contains("ClinicalRecordItems"));

        var refused = await RefusedAsync(() => MergeAsync(Ann, Bo));
        Assert.Equal(("merge_blocked", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(before.Ann, await CountsAsync(Ann)); Assert.Equal(before.Bo, await CountsAsync(Bo));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
    }

    [Fact]
    public async Task The_check_after_the_moves_refuses_a_row_that_disagrees_with_the_row_it_points_at_and_a_row_left_behind_that_should_have_moved()
    {
        await AddAlertAsync("Latex allergy", patient: Ann);
        var alertKey = (await ScalarGuidAsync("SELECT [Id] AS [Value] FROM [SafetyAlerts] WHERE [PatientId] = @p", new SqlParameter("@p", Ann))).ToString().ToUpperInvariant();
        await using (var db = Fixture.CreateContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("EXEC sp_set_session_context N'alveara.patient_merge', 1");
            await db.Database.ExecuteSqlRawAsync("UPDATE [SafetyAlertVersions] SET [PatientId] = @b WHERE [PatientId] = @a", new SqlParameter("@a", Ann), new SqlParameter("@b", Bo));     // the versions move, their alert does not
            await db.Database.ExecuteSqlRawAsync("UPDATE [Encounters] SET [PatientId] = @b WHERE [PatientId] = @a", new SqlParameter("@a", Ann), new SqlParameter("@b", Bo));
            await tx.CommitAsync();
        }
        var stays =new MergePlan([], [], [], new() { ["SafetyAlerts"] = new() { [alertKey] = "test" } }, []);
        var disagree = await RefusedAsync(() => WithDb(async db => { await new PatientMergeService(db, Clock).VerifyAsync(new PatientMergeCatalog(db.Model), stays, Ann, Bo, default); return 0; }));
        Assert.Equal(("merge_inconsistent", 500), (disagree.Code, disagree.StatusCode));
        Assert.Contains("SafetyAlertVersions", disagree.Message); Assert.Contains("disagree", disagree.Message);

        var forgotten = await RefusedAsync(() => WithDb(async db => { await new PatientMergeService(db, Clock).VerifyAsync(new PatientMergeCatalog(db.Model), new MergePlan([], [], [], [], []), Ann, Bo, default); return 0; }));
        Assert.Equal("merge_inconsistent", forgotten.Code);
        Assert.Contains("still holds", forgotten.Message);                                                                              // the alert was neither moved nor meant to stay
    }

    private async Task<Guid> ScalarGuidAsync(string sql, params SqlParameter[] parameters) { await using var db = Fixture.CreateContext(); return await db.Database.SqlQueryRaw<Guid>(sql, parameters).FirstAsync(); }

    // ---------- idempotency, refusals and races ----------

    [Fact]
    public async Task A_repeat_of_the_same_request_is_the_merge_already_made_and_the_same_key_for_another_pair_is_refused()
    {
        await GiveChartAsync(Ann, "Gingivitis");
        var first = await MergeAsync(Ann, Bo, key: "merge-1");
        var counts = await CountsAsync(Bo);
        var again = await WithService(s => s.MergeAsync(S.Actor, new MergeRequest(Ann, Bo, "Different words", null, null, null), "merge-1", default));      // no row versions needed: it is already done
        Assert.True(again.Replayed); Assert.Equal(first.MergeId, again.MergeId); Assert.Equal(first.MergedAtUtc, again.MergedAtUtc); Assert.Equal(first.Reason, again.Reason);
        Assert.Equal(first.Moves.Sum(m => m.Rows), again.Moves.Sum(m => m.Rows));
        Assert.Equal(counts, await CountsAsync(Bo));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientMerged'"));

        var other = await S.PatientAsync("Cy", "Kim");
        Assert.Equal("key_reused", (await RefusedAsync(() => WithService(s => s.MergeAsync(S.Actor, new MergeRequest(other, Bo, "x", null, null, null), "merge-1", default)))).Code);
    }

    [Fact]
    public async Task A_merged_record_cannot_be_merged_again_or_kept_and_a_record_cannot_be_merged_into_itself_or_into_nothing()
    {
        await MergeAsync(Ann, Bo);
        var third = await S.PatientAsync("Dee", "Kim");
        Assert.Equal("already_merged", (await RefusedAsync(() => MergeAsync(Ann, third))).Code);
        Assert.Equal("survivor_merged", (await RefusedAsync(() => MergeAsync(third, Ann))).Code);
        Assert.Equal("same_patient", (await RefusedAsync(() => MergeAsync(third, third))).Code);
        Assert.Equal("patient_not_found", (await RefusedAsync(() => MergeAsync(third, Guid.NewGuid()))).Code);
        Assert.Equal("source_required", (await RefusedAsync(() => WithService(s => s.MergeAsync(S.Actor, new MergeRequest(null, Bo, "x", null, null, null), "k", default)))).Code);
        Assert.Equal("survivor_required", (await RefusedAsync(() => WithService(s => s.MergeAsync(S.Actor, new MergeRequest(third, null, "x", null, null, null), "k", default)))).Code);
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
    }

    [Fact]
    public async Task A_merge_needs_a_key_a_reason_and_the_versions_of_both_records_and_refuses_a_record_that_changed_after_it_was_reviewed()
    {
        var preview = await WithService(s => s.PreviewAsync(Ann, Bo, default));
        MergeRequest R(string? reason = "Same person", string? source = null, string? survivor = null) => new(Ann, Bo, reason, source ?? preview.SourceRowVersion, survivor ?? preview.SurvivorRowVersion, null);
        Task<MergeResult> Run(MergeRequest r, string? key = "k1") => WithService(s => s.MergeAsync(S.Actor, r, key, default));

        Assert.Equal("idempotency_key_invalid", (await RefusedAsync(() => Run(R(), null))).Code);
        Assert.Equal("idempotency_key_invalid", (await RefusedAsync(() => Run(R(), new string('k', 101)))).Code);
        Assert.Equal("reason_invalid", (await RefusedAsync(() => Run(R(reason: "  ")))).Code);
        Assert.Equal("reason_invalid", (await RefusedAsync(() => Run(R(reason: new string('r', 501))))).Code);
        Assert.Equal("row_version_required", (await RefusedAsync(() => Run(new MergeRequest(Ann, Bo, "x", null, preview.SurvivorRowVersion, null)))).Code);
        Assert.Equal("row_version_invalid", (await RefusedAsync(() => Run(R(source: "not base64!")))).Code);

        await SetAsync(Bo, "City", "Dallas");                                                                                           // someone edits the survivor after the review
        var stale = await RefusedAsync(() => Run(R()));
        Assert.Equal(("patient_changed", 409), (stale.Code, stale.StatusCode));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [MergedAtUtc] IS NOT NULL"));
    }

    [Fact]
    public async Task Two_requests_with_the_same_key_at_the_same_moment_make_one_merge()
    {
        await GiveChartAsync(Ann, "Gingivitis");
        var preview = await WithService(s => s.PreviewAsync(Ann, Bo, default));
        var request = new MergeRequest(Ann, Bo, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, null);
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => WithService(s => s.MergeAsync(S.Actor, request, "race-1", default))));
        Assert.Single(results.Select(r => r.MergeId).Distinct());
        Assert.Equal(1, results.Count(r => !r.Replayed));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        Assert.Equal(2, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientMerged'"));
    }

    // ---------- failing part way ----------

    [Fact]
    public async Task When_the_audit_write_fails_nothing_is_merged_and_the_same_request_succeeds_once_it_works()
    {
        await GiveChartAsync(Ann, "Gingivitis");
        var before = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_TestAuditDown] ON [AuditLogEntries] AFTER INSERT AS THROW 59000, 'the audit store is down', 1;");
        var preview = await WithService(s => s.PreviewAsync(Ann, Bo, default));
        var request = new MergeRequest(Ann, Bo, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, null);

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => WithService(s => s.MergeAsync(S.Actor, request, "retry-1", default)));
        Assert.Equal(59000, Assert.IsType<SqlException>(failure is DbUpdateException { InnerException: not null } d ? d.InnerException : failure).Number);

        Assert.Equal(before.Ann, await CountsAsync(Ann)); Assert.Equal(before.Bo, await CountsAsync(Bo));                               // every row is still where it was
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [MergedAtUtc] IS NOT NULL OR [IsActive] = 0"));
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_TestAuditDown]");

        var retried = await WithService(s => s.MergeAsync(S.Actor, request, "retry-1", default));                                       // and the same request now works
        Assert.False(retried.Replayed);
        Assert.Equal(0, (await CountsAsync(Ann))["TreatmentPlans.PatientId"]);
    }

    [Fact]
    public async Task The_survivors_guarantor_is_cleared_when_it_was_the_retired_record_and_others_guarantors_follow_the_survivor()
    {
        var dependant = await S.PatientAsync("Eli", "Kim");
        await using (var db = Fixture.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [GuarantorPatientId] = @a WHERE [Id] IN (@b, @c)", new SqlParameter("@a", Ann), new SqlParameter("@b", Bo), new SqlParameter("@c", dependant));
        }
        await MergeAsync(Ann, Bo);
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = @p AND [GuarantorPatientId] IS NOT NULL", new SqlParameter("@p", Bo)));       // not its own guarantor
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = @p AND [GuarantorPatientId] = @s", new SqlParameter("@p", dependant), new SqlParameter("@s", Bo)));
    }

    [Fact]
    public async Task The_household_is_carried_over_when_the_survivor_has_none()
    {
        Guid household;
        await using (var db = Fixture.CreateContext())
        {
            var h = new Household { Id = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow };
            db.Households.Add(h); await db.SaveChangesAsync(); household = h.Id;
            await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [HouseholdId] = @h, [HouseholdRelationship] = 'Parent' WHERE [Id] = @p", new SqlParameter("@h", household), new SqlParameter("@p", Ann));
        }
        var result = await MergeAsync(Ann, Bo);
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = @p AND [HouseholdId] = @h AND [HouseholdRelationship] = 'Parent'", new SqlParameter("@p", Bo), new SqlParameter("@h", household)));
        Assert.Contains(result.Resolutions, r => r.Subject == "Household" && r.Choice == "TookSource");
    }
}
