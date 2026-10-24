using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Recall;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C01, the work-queue columns, the recall settings and the patient recall plans on their own: rows written straight to the database, around any service. A task has a priority and a kind from closed sets; may be
/// unassigned or made by the system (no creator) only when it is not a Manual task; a Recall task has a key and an UnscheduledTreatment task a key and its plan item; the key is unique and plain; a task may be closed
/// by the system; its kind, creator and source never change; an appointment link and a plan item belong to the task's own patient (51211); the recall settings are one row that is never deleted (51214) and stay inside the limits
/// the code uses; a patient's recall plan means something; and the migration rolls back and forward while no work-queue data exists and refuses to roll back over it (51215).
/// </summary>
public class WorkQueueSchemaTests : TreatmentPlanTestBase
{
    private const string BeforeWorkQueue = "20261024005717_AddFollowUps";

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

    private async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusalAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        return await Refusal(() => db.Database.ExecuteSqlAsync(sql));
    }

    private async Task<int> CountAsync<T>() where T : class { await using var db = Fixture.CreateContext(); return await db.Set<T>().CountAsync(); }

    private static string Key(string tag = "x") => $"recall:{Guid.NewGuid():N}:{tag}";

    private FollowUpTask Manual(Action<FollowUpTask>? change = null)
    {
        var t = new FollowUpTask
        {
            Id = Guid.NewGuid(), PatientId = Ann, AssigneeUserId = Other, Title = "Call the patient", DueAtUtc = DateTimeOffset.UtcNow.AddDays(7), Status = FollowUpStatuses.Open, CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(t);
        return t;
    }

    /// <summary>A task the system made for a patient who is due a recall: no creator, no owner.</summary>
    private FollowUpTask SystemRecall(Action<FollowUpTask>? change = null) => Manual(t =>
    {
        t.Kind = FollowUpKinds.Recall; t.CreatedByUserId = null; t.AssigneeUserId = null; t.SourceKey = Key(); t.Title = "Recall due";
        change?.Invoke(t);
    });

    private async Task<TreatmentPlanItem> PlanItemAsync(Guid patient)
    {
        var plan = new TreatmentPlan { Id = Guid.NewGuid(), PatientId = patient, IdempotencyKey = $"plan-{Guid.NewGuid():N}", Title = "Plan", Status = TreatmentPlanStatuses.Proposed, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        var procedure = await ProcedureAsync();
        var diagnosis = await DiagnosisAsync(patient);
        var item = new TreatmentPlanItem
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = patient, ItemNumber = 1, IdempotencyKey = "item-1", DiagnosisId = diagnosis, ProcedureId = procedure.Summary.Id,
            ProcedureVersionId = procedure.Versions[0].VersionId, Fee = procedure.Versions[0].Fee, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor,
        };
        await AddAsync(plan, item);
        return item;
    }

    private async Task<Guid> AppointmentAsync(Guid patient)
    {
        var result = await S.ScheduleAsync(S.Request(patient, new DateTime(2027, 3, 2, 9, 0, 0)));
        return result.Appointment.Id;
    }

    // ---------- defaults and the closed sets ----------

    [Fact]
    public async Task A_task_made_the_old_way_is_a_normal_priority_manual_task_and_the_recall_settings_start_off_with_the_defaults()
    {
        await AddAsync(Manual());
        await using var db = Fixture.CreateContext();
        var task = await db.FollowUpTasks.AsNoTracking().SingleAsync();
        Assert.Equal((FollowUpPriorities.Normal, FollowUpKinds.Manual, false, null, null), (task.Priority, task.Kind, task.ClosedBySystem, task.AppointmentId, task.SourceKey));
        var settings = await db.RecallSettings.AsNoTracking().SingleAsync();
        Assert.Equal((1, false, 180, 30, 24, 7), (settings.Id, settings.Enabled, settings.DefaultIntervalDays, settings.LeadDays, settings.MaxVisitAgeMonths, settings.DueSoonDays));
        Assert.Equal(0, await db.PatientRecallPlans.CountAsync());
    }

    [Theory]
    [InlineData("low")]
    [InlineData("Critical")]
    [InlineData("")]
    public async Task A_priority_is_one_of_the_four_exact_names(string priority)
    {
        Assert.Equal(547, (await AddRefusedAsync(Manual(t => t.Priority = priority))).Number);
    }

    [Fact]
    public async Task Every_defined_priority_is_accepted_and_sorts_most_urgent_first()
    {
        foreach (var p in FollowUpPriorities.All) await AddAsync(Manual(t => t.Priority = p));
        Assert.Equal(4, await CountAsync<FollowUpTask>());
        Assert.Equal(new[] { "Urgent", "High", "Normal", "Low" }, FollowUpPriorities.All.OrderBy(FollowUpPriorities.Weight).ToArray());
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("Referral")]
    [InlineData("")]
    public async Task A_kind_is_one_of_the_three_exact_names(string kind)
    {
        Assert.Equal(547, (await AddRefusedAsync(Manual(t => t.Kind = kind))).Number);
    }

    // ---------- who made it, who owns it, what it is about ----------

    [Fact]
    public async Task A_task_the_system_made_has_no_creator_and_no_owner_and_can_wait_for_someone_to_take_it()
    {
        await AddAsync(SystemRecall());
        await using var db = Fixture.CreateContext();
        var t = await db.FollowUpTasks.AsNoTracking().SingleAsync();
        Assert.Equal((null, null, FollowUpKinds.Recall, FollowUpStatuses.Open), (t.CreatedByUserId, t.AssigneeUserId, t.Kind, t.Status));
    }

    [Fact]
    public async Task A_manual_task_still_needs_a_creator_but_may_now_be_unassigned()
    {
        Assert.Equal(547, (await AddRefusedAsync(Manual(t => t.CreatedByUserId = null))).Number);
        await AddAsync(Manual(t => t.AssigneeUserId = null));
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }

    [Fact]
    public async Task The_shape_of_each_kind_is_enforced_a_manual_task_has_no_source_a_recall_has_a_key_and_a_treatment_task_has_a_key_and_its_plan_item()
    {
        var item = await PlanItemAsync(Ann);
        Assert.Equal(547, (await AddRefusedAsync(Manual(t => t.SourceKey = Key()))).Number);                                                       // a manual task has no system key
        Assert.Equal(547, (await AddRefusedAsync(Manual(t => t.SourcePlanItemId = item.Id))).Number);                                              // nor a plan item
        Assert.Equal(547, (await AddRefusedAsync(SystemRecall(t => t.SourceKey = null))).Number);                                                  // a recall needs its key
        Assert.Equal(547, (await AddRefusedAsync(SystemRecall(t => t.SourcePlanItemId = item.Id))).Number);                                        // and has no plan item
        Assert.Equal(547, (await AddRefusedAsync(SystemRecall(t => { t.Kind = FollowUpKinds.UnscheduledTreatment; }))).Number);                    // a treatment task needs its plan item
        Assert.Equal(547, (await AddRefusedAsync(SystemRecall(t => { t.Kind = FollowUpKinds.UnscheduledTreatment; t.SourcePlanItemId = item.Id; t.SourceKey = null; }))).Number);
        await AddAsync(SystemRecall(t => { t.Kind = FollowUpKinds.UnscheduledTreatment; t.SourcePlanItemId = item.Id; t.SourceKey = FollowUpRules.UnscheduledSourceKey(item.Id, 1); }));
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }

    [Theory]
    [InlineData("recall-with-hyphen")]
    [InlineData("recall with space")]
    [InlineData("recall:é")]
    [InlineData("")]
    public async Task A_source_key_holds_only_letters_digits_colons_dots_and_underscores(string key)
    {
        Assert.Equal(547, (await AddRefusedAsync(SystemRecall(t => t.SourceKey = key))).Number);
    }

    [Fact]
    public async Task The_helper_keys_are_plain_and_a_source_key_is_unique_so_a_retry_cannot_make_the_same_task_twice()
    {
        var patient = Guid.NewGuid();
        var recall = FollowUpRules.RecallSourceKey(patient, new DateOnly(2027, 1, 15));
        Assert.Equal($"recall:{patient:N}:20270115", recall);
        Assert.Matches("^[A-Za-z0-9:._]+$", recall);
        Assert.True(recall.Length <= FollowUpRules.SourceKeyMaxLength);
        Assert.True(FollowUpRules.UnscheduledSourceKey(Guid.NewGuid(), 12).Length <= FollowUpRules.SourceKeyMaxLength);

        var key = Key("once");
        await AddAsync(SystemRecall(t => t.SourceKey = key));
        Assert.True((await AddRefusedAsync(SystemRecall(t => t.SourceKey = key))).Number is 2601 or 2627);
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }

    // ---------- closing it ----------

    [Fact]
    public async Task The_system_can_close_a_task_and_a_closed_task_says_who_or_the_system()
    {
        await AddAsync(SystemRecall(t => { t.Status = FollowUpStatuses.Cancelled; t.ClosedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1); t.ClosedBySystem = true; t.ClosureNote = "The patient was booked"; }));
        Assert.Equal(547, (await AddRefusedAsync(SystemRecall(t => { t.Status = FollowUpStatuses.Cancelled; t.ClosedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1); }))).Number);                                      // nobody
        Assert.Equal(547, (await AddRefusedAsync(SystemRecall(t => { t.Status = FollowUpStatuses.Cancelled; t.ClosedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1); t.ClosedBySystem = true; t.ClosedByUserId = S.Actor; }))).Number);   // both
        Assert.Equal(547, (await AddRefusedAsync(SystemRecall(t => t.ClosedBySystem = true))).Number);                                                                                                            // open but closed by the system
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }

    // ---------- links to the same patient ----------

    [Fact]
    public async Task An_appointment_link_must_be_an_appointment_of_the_same_patient()
    {
        var annsAppointment = await AppointmentAsync(Ann);
        await AddAsync(Manual(t => t.AppointmentId = annsAppointment));
        Assert.Equal(51211, (await AddRefusedAsync(Manual(t => { t.PatientId = Bo; t.AppointmentId = annsAppointment; }))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Manual(t => t.AppointmentId = Guid.NewGuid()))).Number);                       // and it must exist
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }

    [Fact]
    public async Task A_treatment_task_names_a_plan_item_of_its_own_patient()
    {
        var annsItem = await PlanItemAsync(Ann);
        Assert.Equal(51211, (await AddRefusedAsync(SystemRecall(t => { t.Kind = FollowUpKinds.UnscheduledTreatment; t.PatientId = Bo; t.SourcePlanItemId = annsItem.Id; t.SourceKey = Key(); }))).Number);
        Assert.Equal(547, (await AddRefusedAsync(SystemRecall(t => { t.Kind = FollowUpKinds.UnscheduledTreatment; t.SourcePlanItemId = Guid.NewGuid(); t.SourceKey = Key(); }))).Number);
        Assert.Equal(0, await CountAsync<FollowUpTask>());
    }

    // ---------- what may and may not change ----------

    [Fact]
    public async Task An_open_task_can_change_owner_priority_due_time_and_appointment_but_never_its_kind_creator_or_source()
    {
        var plan = await PlanItemAsync(Ann);
        var appointment = await AppointmentAsync(Ann);
        var system = SystemRecall();
        var manual = Manual();
        await AddAsync(system, manual);
        await using (var db = Fixture.CreateContext())
        {
            var t = await db.FollowUpTasks.SingleAsync(x => x.Id == system.Id);
            t.AssigneeUserId = Other; t.Priority = FollowUpPriorities.Urgent; t.AppointmentId = appointment; t.DueAtUtc = t.DueAtUtc.AddDays(3);
            await db.SaveChangesAsync();                                                                                                                 // taking, prioritizing, linking and rescheduling are all fine
        }
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [Kind] = N'UnscheduledTreatment', [SourcePlanItemId] = {plan.Id} WHERE [Id] = {system.Id}")).Number);   // a valid shape, but the kind is fixed
        Assert.Equal(547, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [Kind] = N'Manual', [SourceKey] = NULL WHERE [Id] = {system.Id}")).Number);                              // and a manual task needs a creator
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [SourceKey] = N'recall:other' WHERE [Id] = {system.Id}")).Number);
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [CreatedByUserId] = {S.Actor} WHERE [Id] = {system.Id}")).Number);
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [CreatedByUserId] = NULL, [Kind] = N'Recall', [SourceKey] = N'recall:x' WHERE [Id] = {manual.Id}")).Number);
        Assert.Equal(547, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [SourcePlanItemId] = {plan.Id} WHERE [Id] = {manual.Id}")).Number);          // a manual task has no plan item (the shape check answers before the trigger)
    }

    [Fact]
    public async Task Linking_a_task_to_another_patients_appointment_later_is_refused_too()
    {
        var annsAppointment = await AppointmentAsync(Ann);
        var bos = Manual(t => t.PatientId = Bo);
        await AddAsync(bos);
        Assert.Equal(51211, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [AppointmentId] = {annsAppointment} WHERE [Id] = {bos.Id}")).Number);
    }

    // ---------- events and deliveries ----------

    [Fact]
    public async Task The_new_event_kinds_exist_and_only_the_systems_own_kinds_have_no_person_behind_them()
    {
        var task = SystemRecall();
        await AddAsync(task);
        foreach (var kind in new[] { FollowUpEventKinds.SystemCreated, FollowUpEventKinds.SystemClosed, FollowUpEventKinds.Reminded })
            await AddAsync(new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = task.Id, Kind = kind, ActorUserId = null, OccurredAtUtc = DateTimeOffset.UtcNow });
        foreach (var kind in new[] { FollowUpEventKinds.Assigned, FollowUpEventKinds.PriorityChanged })
        {
            await AddAsync(new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = task.Id, Kind = kind, ActorUserId = S.Actor, OccurredAtUtc = DateTimeOffset.UtcNow });
            Assert.Equal(547, (await AddRefusedAsync(new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = task.Id, Kind = kind, ActorUserId = null, OccurredAtUtc = DateTimeOffset.UtcNow })).Number);
        }
        Assert.Equal(5, await CountAsync<FollowUpTaskEvent>());
        Assert.Equal(new[] { "Reminded", "SystemClosed", "SystemCreated" }, FollowUpEventKinds.BySystem.OrderBy(k => k).ToArray());
    }

    [Fact]
    public async Task A_reminder_for_an_unassigned_task_is_recorded_with_its_own_outcome()
    {
        var task = SystemRecall();
        await AddAsync(task);
        await AddAsync(new FollowUpReminderDelivery { Id = Guid.NewGuid(), TaskId = task.Id, DueAtUtc = task.DueAtUtc, Channel = FollowUpChannels.InApp, RecipientUserId = Other, Outcome = FollowUpDeliveryOutcomes.Unassigned, CreatedAtUtc = DateTimeOffset.UtcNow });
        Assert.Equal(1, await CountAsync<FollowUpReminderDelivery>());
    }

    // ---------- the recall settings ----------

    [Fact]
    public async Task The_recall_settings_are_one_row_that_is_never_deleted()
    {
        Assert.Equal(51214, (await SqlRefusalAsync($"DELETE FROM [RecallSettings]")).Number);
        Assert.Equal(547, (await AddRefusedAsync(new RecallSettings { Id = 2, DefaultIntervalDays = 180, LeadDays = 30, MaxVisitAgeMonths = 24, DueSoonDays = 7, UpdatedAtUtc = DateTimeOffset.UtcNow })).Number);
        Assert.Equal(1, await CountAsync<RecallSettings>());
    }

    [Theory]
    [InlineData("DefaultIntervalDays", 0, false)]
    [InlineData("DefaultIntervalDays", 1, true)]
    [InlineData("DefaultIntervalDays", 3650, true)]
    [InlineData("DefaultIntervalDays", 3651, false)]
    [InlineData("LeadDays", -1, false)]
    [InlineData("LeadDays", 0, true)]
    [InlineData("LeadDays", 365, true)]
    [InlineData("LeadDays", 366, false)]
    [InlineData("MaxVisitAgeMonths", 0, false)]
    [InlineData("MaxVisitAgeMonths", 1, true)]
    [InlineData("MaxVisitAgeMonths", 120, true)]
    [InlineData("MaxVisitAgeMonths", 121, false)]
    [InlineData("DueSoonDays", 0, false)]
    [InlineData("DueSoonDays", 1, true)]
    [InlineData("DueSoonDays", 30, true)]
    [InlineData("DueSoonDays", 31, false)]
    public async Task Each_recall_setting_stays_inside_its_limits_at_both_ends(string column, int value, bool allowed)
    {
        await using var db = Fixture.CreateContext();
        var sql = $"UPDATE [RecallSettings] SET [{column}] = {value}";
        if (allowed) { await db.Database.ExecuteSqlRawAsync(sql); Assert.Equal(value, await db.Database.SqlQuery<int>(System.Runtime.CompilerServices.FormattableStringFactory.Create($"SELECT [{column}] AS [Value] FROM [RecallSettings]")).SingleAsync()); }
        else Assert.Equal(547, (await Refusal(() => db.Database.ExecuteSqlRawAsync(sql))).Number);
    }

    // ---------- a patient's own recall plan ----------

    private PatientRecallPlan Plan(Guid patient, int? interval = 90, bool optedOut = false, string? reason = null) =>
        new() { PatientId = patient, IntervalDays = interval, OptedOut = optedOut, Reason = reason, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor };

    [Fact]
    public async Task A_patients_plan_has_an_interval_or_an_opt_out_or_both_and_nothing_else_is_stored()
    {
        await AddAsync(Plan(Ann, interval: 90), Plan(Bo, interval: null, optedOut: true, reason: "Moved to another practice"));
        var other = await S.PatientAsync("Cy", "Ng");
        Assert.Equal(547, (await AddRefusedAsync(Plan(other, interval: null, optedOut: false))).Number);        // a row that means nothing would not exist
        Assert.Equal(2, await CountAsync<PatientRecallPlan>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3651)]
    public async Task A_patients_own_interval_stays_inside_the_limits(int days)
    {
        Assert.Equal(547, (await AddRefusedAsync(Plan(Ann, interval: days))).Number);
    }

    [Fact]
    public async Task A_patient_has_one_plan_a_real_patient_and_a_plain_short_reason()
    {
        await AddAsync(Plan(Ann));
        Assert.True((await AddRefusedAsync(Plan(Ann, interval: 120))).Number is 2601 or 2627);
        Assert.Equal(547, (await AddRefusedAsync(Plan(Guid.NewGuid()))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Plan(Bo, reason: "   "))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Plan(Bo, reason: "bell\u0007"))).Number);
        Assert.NotEqual(0, (await AddRefusedAsync(Plan(Bo, reason: new string('r', 201)))).Number);
        Assert.Equal(1, await CountAsync<PatientRecallPlan>());
    }

    // ---------- the migration ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_work_queue_data_exists_and_refuses_to_roll_back_over_it()
    {
        await AddAsync(Manual());                                                                                                                         // an ordinary STORY-009 task does not block a rollback
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeWorkQueue);
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('RecallSettings','PatientRecallPlans')").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('FollowUpTasks') AND name IN ('Priority','Kind','AppointmentId','SourceKey','ClosedBySystem')").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM FollowUpTasks").SingleAsync());                           // the old task is untouched
        }
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('RecallSettings','PatientRecallPlans')").SingleAsync());
            Assert.Equal((1, false, 180), (await db.RecallSettings.CountAsync(), (await db.RecallSettings.SingleAsync()).Enabled, (await db.RecallSettings.SingleAsync()).DefaultIntervalDays));   // seeded again after the roll forward
            var old = await db.FollowUpTasks.AsNoTracking().SingleAsync();
            Assert.Equal((FollowUpPriorities.Normal, FollowUpKinds.Manual), (old.Priority, old.Kind));
        }

        await AddAsync(SystemRecall());
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeWorkQueue); });
        Assert.Equal(51215, e.Number);
        Assert.Equal(2, await CountAsync<FollowUpTask>());
    }
}
