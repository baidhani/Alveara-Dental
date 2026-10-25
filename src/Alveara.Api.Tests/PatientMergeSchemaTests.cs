using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-017, the merge schema on its own: rows written straight to the database, around any service. The lineage (who was merged into whom, by whom, when, why, what moved, what was decided) is append-only (51230); a merged
/// patient cannot be the survivor of another merge (51231); a patient is marked merged only by a lineage row with the same time (51232) and then never changes (51233). The narrow exception the merge service uses: inside a
/// transaction that has set the session value alveara.patient_merge to 1, the immutable-record triggers stand aside for an UPDATE of a patient id and the merge-scope trigger of the table refuses anything else (51234).
/// Without the value everything is refused as before. The migration rolls back and forward while no merge exists and refuses to roll back over one (51239).
/// </summary>
public class PatientMergeSchemaTests : TreatmentPlanTestBase
{
    private const string BeforeMerges = "20261024223402_AddExternalCases";
    private static readonly DateOnly Today = new(2026, 10, 25);

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private Task<Microsoft.Data.SqlClient.SqlException> AddRefusedAsync(params object[] rows) => Refusal(() => AddAsync(rows));

    private async Task<int> SqlAsync(FormattableString sql) { await using var db = Fixture.CreateContext(); return await db.Database.ExecuteSqlAsync(sql); }
    private async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusalAsync(FormattableString sql) => await Refusal(() => SqlAsync(sql));
    private async Task<int> CountAsync<T>() where T : class { await using var db = Fixture.CreateContext(); return await db.Set<T>().CountAsync(); }

    /// <summary>Runs <paramref name="body"/> inside one transaction on one connection after setting the merge value, the way the merge service does, and clears nothing: the connection's own reset does it.</summary>
    private async Task<T> InMergeAsync<T>(Func<AlveraDbContext, Task<T>> body, int flag = 1)
    {
        await using var db = Fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"EXEC sp_set_session_context N'alveara.patient_merge', {flag}");
        var result = await body(db);
        await tx.CommitAsync();
        return result;
    }

    private Task<Microsoft.Data.SqlClient.SqlException> InMergeRefusalAsync(FormattableString sql, int flag = 1) => Refusal(() => InMergeAsync(db => db.Database.ExecuteSqlAsync(sql), flag));

    private async Task<int> QueryIntAsync(FormattableString sql) { await using var db = Fixture.CreateContext(); return await db.Database.SqlQuery<int>(sql).SingleAsync(); }

    private PatientMerge Lineage(Guid source, Guid survivor, Action<PatientMerge>? change = null)
    {
        var m = new PatientMerge { Id = Guid.NewGuid(), MergeKey = $"merge-{Guid.NewGuid():N}", SourcePatientId = source, SurvivorPatientId = survivor, MergedByUserId = S.Actor, MergedAtUtc = DateTimeOffset.UtcNow, Reason = "Same person registered twice" };
        change?.Invoke(m);
        return m;
    }

    /// <summary>Two new patients, the lineage of the first merged into the second, and the first marked merged: a complete merge as the service will write it.</summary>
    private async Task<(Guid Source, Guid Survivor, PatientMerge Lineage)> MergedPairAsync()
    {
        var source = await S.PatientAsync($"Src{Guid.NewGuid():N}"[..10], "Merge"); var survivor = await S.PatientAsync($"Sur{Guid.NewGuid():N}"[..10], "Merge");
        var m = Lineage(source, survivor); await AddAsync(m);
        await SqlAsync($"UPDATE [Patients] SET [IsActive] = 0, [MergedAtUtc] = {m.MergedAtUtc} WHERE [Id] = {source}");
        return (source, survivor, m);
    }

    private TreatmentPlan Plan(Guid patient) => new() { Id = Guid.NewGuid(), PatientId = patient, IdempotencyKey = $"plan-{Guid.NewGuid():N}", Title = "Plan", Status = TreatmentPlanStatuses.Proposed, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };

    private PatientDocument Document(Guid patient) => new()
    {
        Id = Guid.NewGuid(), PatientId = patient, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Panoramic film.", OriginalFileName = "pano.png", DetectedType = "png",
        ContentType = "image/png", SizeBytes = 2048, Sha256 = new string('a', 64), BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('b', 64),
    };

    private async Task<Guid> CompletedReferralAsync(Guid patient)
    {
        var party = new ExternalParty { Id = Guid.NewGuid(), Kind = ExternalPartyKinds.ReferralProvider, Name = $"Party {Guid.NewGuid():N}", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor, UpdatedAtUtc = DateTimeOffset.UtcNow };
        var c = new ReferralCase
        {
            Id = Guid.NewGuid(), PatientId = patient, PartyId = party.Id, Reason = "Evaluation of an impacted wisdom tooth", Status = "Completed", SentOn = Today.AddDays(-3), ReceivedOn = Today.AddDays(-1), IdempotencyKey = $"ref-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor, UpdatedAtUtc = DateTimeOffset.UtcNow, ClosedAtUtc = DateTimeOffset.UtcNow, ClosedByUserId = S.Actor,
        };
        await AddAsync(party, c);
        return c.Id;
    }

    // ---------- the lineage ----------

    [Fact]
    public async Task A_lineage_names_two_different_patients_a_reason_and_a_key_and_a_patient_is_retired_once()
    {
        var a = await S.PatientAsync("Ada", "Merge"); var b = await S.PatientAsync("Bea", "Merge"); var c = await S.PatientAsync("Cy", "Merge");
        var first = Lineage(a, b); await AddAsync(first);
        Assert.Equal(1, await CountAsync<PatientMerge>());
        Assert.Equal(547, (await AddRefusedAsync(Lineage(c, c))).Number);                                              // into itself
        Assert.Equal(547, (await AddRefusedAsync(Lineage(c, b, m => m.Reason = "  "))).Number);                       // no reason
        Assert.Equal(547, (await AddRefusedAsync(Lineage(c, b, m => m.MergeKey = " "))).Number);                      // no key
        Assert.Equal(547, (await AddRefusedAsync(Lineage(c, b, m => m.MergedAtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Lineage(c, Guid.NewGuid()))).Number);                                 // a survivor that is not a patient
        Assert.Equal(547, (await AddRefusedAsync(Lineage(c, b, m => m.MergedByUserId = Guid.NewGuid()))).Number);      // a user that does not exist
        Assert.Equal(2601, (await AddRefusedAsync(Lineage(c, b, m => m.MergeKey = first.MergeKey))).Number);           // the same key is the same merge
        Assert.Equal(2601, (await AddRefusedAsync(Lineage(a, c))).Number);                                             // a patient is retired once
        Assert.Equal(1, await CountAsync<PatientMerge>());
    }

    [Fact]
    public async Task The_lineage_its_moves_and_its_resolutions_are_never_changed_or_deleted()
    {
        var (_, _, m) = await MergedPairAsync();
        await AddAsync(new PatientMergeMove { MergeId = m.Id, TableName = "Appointments", ColumnName = "PatientId", RowsMoved = 3 },
            new PatientMergeResolution { Id = Guid.NewGuid(), MergeId = m.Id, Kind = PatientMergeResolutionKinds.Field, Subject = "Phone", Choice = PatientMergeChoices.TookSource, ReplacedValue = "(555) 010-0100" });
        Assert.Equal(51230, (await SqlRefusalAsync($"UPDATE [PatientMerges] SET [Reason] = N'Changed' WHERE [Id] = {m.Id}")).Number);
        Assert.Equal(547, (await SqlRefusalAsync($"DELETE FROM [PatientMerges] WHERE [Id] = {m.Id}")).Number);                                 // its moves and resolutions point at it
        var (_, _, bare) = await MergedPairAsync();
        Assert.Equal(51230, (await SqlRefusalAsync($"DELETE FROM [PatientMerges] WHERE [Id] = {bare.Id}")).Number);
        Assert.Equal(51230, (await SqlRefusalAsync($"UPDATE [PatientMerges] SET [Reason] = N'Changed' WHERE [Id] = {bare.Id}")).Number);
        Assert.Equal(51230, (await SqlRefusalAsync($"UPDATE [PatientMergeMoves] SET [RowsMoved] = 0 WHERE [MergeId] = {m.Id}")).Number);
        Assert.Equal(51230, (await SqlRefusalAsync($"DELETE FROM [PatientMergeMoves] WHERE [MergeId] = {m.Id}")).Number);
        Assert.Equal(51230, (await SqlRefusalAsync($"UPDATE [PatientMergeResolutions] SET [Subject] = N'Email' WHERE [MergeId] = {m.Id}")).Number);
        Assert.Equal(51230, (await SqlRefusalAsync($"DELETE FROM [PatientMergeResolutions] WHERE [MergeId] = {m.Id}")).Number);
        Assert.Equal(1, await CountAsync<PatientMergeMove>());
        Assert.Equal(1, await CountAsync<PatientMergeResolution>());
    }

    [Fact]
    public async Task A_move_counts_rows_once_per_column_and_a_resolution_is_a_field_kept_or_taken_or_a_record_left_behind()
    {
        var (_, _, m) = await MergedPairAsync();
        await AddAsync(new PatientMergeMove { MergeId = m.Id, TableName = "Appointments", ColumnName = "PatientId", RowsMoved = 0 });                        // zero is recorded too
        Assert.Equal(547, (await AddRefusedAsync(new PatientMergeMove { MergeId = m.Id, TableName = "Notes", ColumnName = "PatientId", RowsMoved = -1 })).Number);
        Assert.Equal(547, (await AddRefusedAsync(new PatientMergeMove { MergeId = Guid.NewGuid(), TableName = "Notes", ColumnName = "PatientId", RowsMoved = 1 })).Number);
        Assert.Equal(2627, (await AddRefusedAsync(new PatientMergeMove { MergeId = m.Id, TableName = "Appointments", ColumnName = "PatientId", RowsMoved = 5 })).Number);

        PatientMergeResolution R(string kind, string subject, string choice, string? replaced = null) => new() { Id = Guid.NewGuid(), MergeId = m.Id, Kind = kind, Subject = subject, Choice = choice, ReplacedValue = replaced };
        await AddAsync(R("Field", "Phone", "TookSource", "(555) 010-0100"), R("Field", "Email", "KeptSurvivor"), R("Record", "BillingAccounts:" + Guid.NewGuid(), "LeftWithSource"));
        Assert.Equal(547, (await AddRefusedAsync(R("Field", "City", "LeftWithSource"))).Number);                        // a field is not left behind
        Assert.Equal(547, (await AddRefusedAsync(R("Record", "PerioSessions:x", "TookSource"))).Number);                // a record is only left behind
        Assert.Equal(547, (await AddRefusedAsync(R("Field", "State", "KeptSurvivor", "TX"))).Number);                   // nothing was replaced when the survivor's value was kept
        Assert.Equal(547, (await AddRefusedAsync(R("Record", "PerioSessions:y", "LeftWithSource", "x"))).Number);
        Assert.Equal(547, (await AddRefusedAsync(R("Mystery", "Phone", "TookSource"))).Number);
        Assert.Equal(547, (await AddRefusedAsync(R("Field", " ", "TookSource"))).Number);
        Assert.Equal(2601, (await AddRefusedAsync(R("Field", "Phone", "KeptSurvivor"))).Number);                        // one answer per conflict
        Assert.Equal(3, await CountAsync<PatientMergeResolution>());
    }

    // ---------- the merged mark ----------

    [Fact]
    public async Task A_patient_is_marked_merged_only_by_a_lineage_with_the_same_time_and_then_never_changes()
    {
        var a = await S.PatientAsync("Dee", "Merge"); var b = await S.PatientAsync("Eve", "Merge");
        var when = DateTimeOffset.UtcNow;
        Assert.Equal(51232, (await SqlRefusalAsync($"UPDATE [Patients] SET [IsActive] = 0, [MergedAtUtc] = {when} WHERE [Id] = {a}")).Number);           // no lineage
        var m = Lineage(a, b, x => x.MergedAtUtc = when.AddMinutes(-1)); await AddAsync(m);
        Assert.Equal(51232, (await SqlRefusalAsync($"UPDATE [Patients] SET [IsActive] = 0, [MergedAtUtc] = {when} WHERE [Id] = {a}")).Number);           // another time
        Assert.Equal(547, (await SqlRefusalAsync($"UPDATE [Patients] SET [MergedAtUtc] = {m.MergedAtUtc} WHERE [Id] = {a}")).Number);                  // a merged patient is inactive
        Assert.Equal(1, await SqlAsync($"UPDATE [Patients] SET [IsActive] = 0, [MergedAtUtc] = {m.MergedAtUtc} WHERE [Id] = {a}"));
        Assert.Equal(51233, (await SqlRefusalAsync($"UPDATE [Patients] SET [LastName] = N'Changed' WHERE [Id] = {a}")).Number);
        Assert.Equal(547, (await SqlRefusalAsync($"UPDATE [Patients] SET [IsActive] = 1 WHERE [Id] = {a}")).Number);                                    // never reactivated
        Assert.Equal(51233, (await SqlRefusalAsync($"UPDATE [Patients] SET [MergedAtUtc] = NULL, [IsActive] = 1 WHERE [Id] = {a}")).Number);            // never un-merged
        Assert.Equal(0, await QueryIntAsync($"SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = {b} AND [MergedAtUtc] IS NOT NULL"));
    }

    [Fact]
    public async Task A_patient_that_was_merged_away_cannot_be_the_survivor_but_a_survivor_can_itself_be_merged_later()
    {
        var (source, survivor, _) = await MergedPairAsync();
        var other = await S.PatientAsync("Fay", "Merge");
        Assert.Equal(51231, (await AddRefusedAsync(Lineage(other, source))).Number);                                  // into a retired record
        var chain = Lineage(survivor, other); await AddAsync(chain);                                                   // the survivor retired in turn: a chain, not a cycle
        Assert.Equal(51231, (await AddRefusedAsync(Lineage(await S.PatientAsync("Gus", "Merge"), survivor))).Number);   // and now it is retired too
        Assert.Equal(2, await CountAsync<PatientMerge>());
    }

    // ---------- the narrow exception ----------

    [Fact]
    public async Task Inside_a_merge_a_patient_id_can_be_moved_where_the_record_was_immutable_and_without_the_value_nothing_changes()
    {
        var from = await S.PatientAsync("Hal", "Merge"); var to = await S.PatientAsync("Ida", "Merge");
        var plan = Plan(from); var doc = Document(from); var referral = await CompletedReferralAsync(from);
        await AddAsync(plan, doc);

        // without the value: the tables' own refusals, as before
        Assert.Equal(51086, (await SqlRefusalAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {to} WHERE [Id] = {plan.Id}")).Number);
        Assert.Equal(51183, (await SqlRefusalAsync($"UPDATE [Documents] SET [PatientId] = {to} WHERE [Id] = {doc.Id}")).Number);
        Assert.Equal(51221, (await SqlRefusalAsync($"UPDATE [ReferralCases] SET [PatientId] = {to} WHERE [Id] = {referral}")).Number);
        // with the value set to anything but 1, the same
        Assert.Equal(51086, (await InMergeRefusalAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {to} WHERE [Id] = {plan.Id}", flag: 0)).Number);

        // inside the merge: the plan, the document and the completed (final) referral move
        var moved = await InMergeAsync(async db => new[]
        {
            await db.Database.ExecuteSqlAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {to} WHERE [PatientId] = {from}"),
            await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [PatientId] = {to} WHERE [PatientId] = {from}"),
            await db.Database.ExecuteSqlAsync($"UPDATE [ReferralCases] SET [PatientId] = {to} WHERE [PatientId] = {from}"),
            await db.Database.ExecuteSqlAsync($"UPDATE [Appointments] SET [PatientId] = {to} WHERE [PatientId] = {from}"),              // a table with nothing to move is quiet
        });
        Assert.Equal(new[] { 1, 1, 1, 0 }, moved);
        Assert.Equal(1, await QueryIntAsync($"SELECT COUNT(*) AS [Value] FROM [TreatmentPlans] WHERE [Id] = {plan.Id} AND [PatientId] = {to}"));
        Assert.Equal(1, await QueryIntAsync($"SELECT COUNT(*) AS [Value] FROM [ReferralCases] WHERE [Id] = {referral} AND [PatientId] = {to}"));
    }

    [Fact]
    public async Task Inside_a_merge_nothing_but_the_patient_id_may_be_set_and_no_row_may_be_added_or_removed()
    {
        var from = await S.PatientAsync("Jon", "Merge"); var to = await S.PatientAsync("Kim", "Merge");
        var plan = Plan(from); var doc = Document(from); await AddAsync(plan, doc);
        Assert.Equal(51234, (await InMergeRefusalAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {to}, [Title] = N'Renamed' WHERE [Id] = {plan.Id}")).Number);    // a second column
        Assert.Equal(51234, (await InMergeRefusalAsync($"UPDATE [Documents] SET [Description] = N'Edited' WHERE [Id] = {doc.Id}")).Number);                       // not even the patient id with it
        Assert.Equal(51234, (await InMergeRefusalAsync($"UPDATE [TreatmentPlans] SET [Title] = [Title] WHERE [Id] = {plan.Id}")).Number);                        // a column set to itself still counts
        var recall = new Architecture.Recall.PatientRecallPlan { PatientId = from, IntervalDays = 180, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor }; await AddAsync(recall);   // a table with no trigger of its own
        Assert.Equal(51234, (await InMergeRefusalAsync($"DELETE FROM [PatientRecallPlans] WHERE [PatientId] = {from}")).Number);
        var extra = Plan(from);
        Assert.Equal(51234, (await Refusal(() => InMergeAsync(async db => { db.Add(new Architecture.Recall.PatientRecallPlan { PatientId = to, IntervalDays = 90, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor }); return await db.SaveChangesAsync(); }))).Number);   // no insert
        await Assert.ThrowsAnyAsync<Exception>(() => InMergeAsync(async db => { db.Add(extra); return await db.SaveChangesAsync(); }));
        Assert.Equal(0, await QueryIntAsync($"SELECT COUNT(*) AS [Value] FROM [TreatmentPlans] WHERE [Id] = {extra.Id}"));
        Assert.Equal(1, await QueryIntAsync($"SELECT COUNT(*) AS [Value] FROM [TreatmentPlans] WHERE [Id] = {plan.Id} AND [PatientId] = {from} AND [Title] = N'Plan'"));
    }

    [Fact]
    public async Task The_merge_value_does_not_outlive_its_connection()
    {
        var from = await S.PatientAsync("Lee", "Merge"); var to = await S.PatientAsync("Max", "Merge");
        var plan = Plan(from); await AddAsync(plan);
        await InMergeAsync(db => db.Database.ExecuteSqlAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {to} WHERE [Id] = {plan.Id}"));
        for (var i = 0; i < 5; i++)                                                                                    // whichever pooled connection the next statement gets, the value is gone
            Assert.Equal(51086, (await SqlRefusalAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {from} WHERE [Id] = {plan.Id}")).Number);
    }

    [Fact]
    public async Task A_merged_patients_own_row_can_have_its_guarantor_repointed_inside_a_merge_and_nothing_else_about_it()
    {
        var (source, survivor, _) = await MergedPairAsync();
        var dependant = await S.PatientAsync("Ned", "Merge");
        await SqlAsync($"UPDATE [Patients] SET [GuarantorPatientId] = {source} WHERE [Id] = {dependant}");
        Assert.Equal(51234, (await InMergeRefusalAsync($"UPDATE [Patients] SET [GuarantorPatientId] = {survivor}, [LastName] = N'Changed' WHERE [Id] = {dependant}")).Number);
        Assert.Equal(1, await InMergeAsync(db => db.Database.ExecuteSqlAsync($"UPDATE [Patients] SET [GuarantorPatientId] = {survivor} WHERE [GuarantorPatientId] = {source}")));
        Assert.Equal(1, await QueryIntAsync($"SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = {dependant} AND [GuarantorPatientId] = {survivor}"));
    }

    // ---------- the shape of the exception, from the database's own catalog ----------

    private static readonly string TableList = string.Join(",", PatientOwnedData.Tables.Select(t => $"'{t}'"));

    private async Task<List<string>> NamesAsync(string sql) { await using var db = Fixture.CreateContext(); return await db.Database.SqlQueryRaw<string>(sql).ToListAsync(); }

    [Fact]
    public async Task Every_table_that_holds_a_patient_id_has_a_merge_scope_trigger_that_lists_every_other_column()
    {
        var triggers = await NamesAsync("SELECT name AS [Value] FROM sys.triggers WHERE name LIKE 'TR[_]%[_]MergeScope' ORDER BY name");
        Assert.Equal(PatientOwnedData.Tables.Select(PatientOwnedData.MergeScopeTrigger).Order(StringComparer.Ordinal).ToList(), triggers.Order(StringComparer.Ordinal).ToList());
        foreach (var table in PatientOwnedData.Tables)
        {
            var definition = (await NamesAsync($"SELECT OBJECT_DEFINITION(OBJECT_ID('{PatientOwnedData.MergeScopeTrigger(table)}')) AS [Value]")).Single();
            var columns = await NamesAsync($"SELECT name AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('{table}') AND is_computed = 0 AND system_type_id <> 189");
            var patientColumns = PatientOwnedData.All.Where(c => c.Table == table).Select(c => c.Column).ToHashSet();
            foreach (var column in columns)
                Assert.Equal(!patientColumns.Contains(column), definition.Contains($"UPDATE([{column}])", StringComparison.Ordinal));      // listed unless it is a patient id
        }
    }

    [Fact]
    public async Task Every_update_trigger_on_those_tables_stands_aside_for_a_merge_except_the_merge_scope_triggers_and_the_merged_mark()
    {
        var unpatched = await NamesAsync($@"SELECT DISTINCT t.name AS [Value] FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id = t.object_id JOIN sys.trigger_events e ON e.object_id = t.object_id AND e.type = 2
            WHERE t.parent_class = 1 AND OBJECT_NAME(t.parent_id) IN ({TableList}) AND t.name NOT LIKE 'TR[_]%[_]MergeScope' AND m.definition NOT LIKE N'%alveara.patient_merge%'");
        Assert.Empty(unpatched);                              // a new immutable-record trigger on one of these tables must carry the same line (see the AddPatientMerges migration)
        var patched = await NamesAsync($@"SELECT DISTINCT t.name AS [Value] FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id = t.object_id
            WHERE t.parent_class = 1 AND OBJECT_NAME(t.parent_id) IN ({TableList}) AND t.name NOT LIKE 'TR[_]%[_]MergeScope' AND m.definition LIKE N'%alveara.patient_merge%'");
        Assert.Equal(52, patched.Count);                      // 51 immutable-record triggers and TR_Patients_Merged, which has the line of its own
        Assert.Contains("TR_Patients_Merged", patched);
    }

    // ---------- the migration ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_merge_exists_and_refuses_to_roll_back_over_one()
    {
        var plan = Plan(Ann); await AddAsync(plan);
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeMerges);
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name LIKE 'PatientMerge%'").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('Patients') AND name = 'MergedAtUtc'").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name LIKE 'TR[_]%[_]MergeScope' OR name = 'TR_Patients_Merged'").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.sql_modules WHERE definition LIKE N'%alveara.patient_merge%'").SingleAsync());           // every trigger is back as it was
        }
        Assert.Equal(51086, (await SqlRefusalAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {Bo} WHERE [Id] = {plan.Id}")).Number);                           // and still refuses
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(3, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name LIKE 'PatientMerge%'").SingleAsync());
            Assert.Equal(54, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name LIKE 'TR[_]%[_]MergeScope'").SingleAsync());
        }
        Assert.Equal(51086, (await SqlRefusalAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {Bo} WHERE [Id] = {plan.Id}")).Number);

        await MergedPairAsync();
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeMerges); });
        Assert.Equal(51239, e.Number);
        Assert.Equal(1, await CountAsync<PatientMerge>());
    }
}
