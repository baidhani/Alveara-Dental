using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (migrations M1 to M3), the database on its own: rows are written straight to the tables, around any service.
/// - M1: the two visit settings default to required (including the existing settings row); the cancel category is one of the controlled list, case-sensitive, and only on a cancelled
///   appointment; an event reason is never blank.
/// - M2: <c>appointment.provider_reassigned</c> is accepted only with a source key and no patient, message or document.
/// - M3: a checkout outcome is accepted only as a known (item, outcome) pair, with a reason where needed, a ledger entry exactly for the verified financial outcomes, a linked appointment only on
///   the follow-up item; it is never updated or deleted (51282); a correction supersedes an earlier outcome of the same visit and item (51283), at most once.
/// - Each migration rolls back and forward on empty tables and refuses to roll back over recorded data (51280, 51281, 51284), leaving the data in place.
/// </summary>
public class VisitCheckoutMigrationTests : SafetyTestBase
{
    private const string BeforeM1 = "20261112210440_AddLoginPresentation";
    private const string BeforeM2 = "20261114162213_AddVisitWorkflowSettingsAndCancelCategory";
    private const string BeforeM3 = "20261114162254_AddProviderReassignedNotification";

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private async Task SqlAsync(string sql, params object[] args)
    {
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync(sql, args);
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private static void Constraint(Microsoft.Data.SqlClient.SqlException e, string name) =>
        Assert.True(e.Number == 547 && e.Message.Contains(name), $"expected {name}, got {e.Number}: {e.Message}");

    private Task MigrateToAsync(string target) => WithMigrator(m => m.MigrateAsync(target));

    private async Task WithMigrator(Func<IMigrator, Task> action)
    {
        await using var db = Fixture.CreateContext();
        await action(db.GetInfrastructure().GetRequiredService<IMigrator>());
    }

    private async Task<Guid> AppointmentAsync() => (await S.ScheduleAsync(S.Request(Ann, At(9)))).Appointment.Id;

    private VisitCheckoutOutcome Outcome(Guid appointment, string item, string outcome, Action<VisitCheckoutOutcome>? change = null)
    {
        var o = new VisitCheckoutOutcome
        {
            Id = Guid.NewGuid(), AppointmentId = appointment, ItemCode = item, OutcomeCode = outcome, ActorUserId = S.Actor, RecordedAtUtc = DateTimeOffset.UtcNow,
            IdempotencyKey = $"k-{Guid.NewGuid():N}",
        };
        change?.Invoke(o);
        return o;
    }

    private async Task<int> OutcomeCountAsync()
    {
        await using var db = Fixture.CreateContext();
        return await db.VisitCheckoutOutcomes.CountAsync();
    }

    private Notification Reassigned(Action<Notification>? change = null)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(), RecipientUserId = Other, Kind = NotificationKinds.AppointmentProviderReassigned, Priority = NotificationPriorities.Urgent,
            SourceKey = $"AppointmentProviderReassigned:{Guid.NewGuid():N}:{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(n);
        return n;
    }

    // ---------- M1 ----------

    [Fact]
    public async Task Both_visit_settings_default_to_required_for_a_new_and_an_existing_settings_row()
    {
        await SqlAsync("DELETE FROM [PracticeSettings]");
        await SqlAsync("INSERT INTO [PracticeSettings] ([Id], [Name], [UpdatedAtUtc]) VALUES ({0}, N'Practice', SYSUTCDATETIME())", PracticeSettings.SingletonId);
        await using var db = Fixture.CreateContext();
        var row = await db.PracticeSettings.SingleAsync();
        Assert.True(row.ConfirmationRequired);
        Assert.True(row.MarkReadyRequired);
    }

    [Theory]
    [InlineData("patient")]
    [InlineData("office")]
    [InlineData("provider")]
    [InlineData("weather")]
    [InlineData("other")]
    public async Task A_cancelled_appointment_takes_each_controlled_category(string category)
    {
        var id = await AppointmentAsync();
        await SqlAsync("UPDATE [Appointments] SET [Status] = 'Cancelled', [CancelCategory] = {0} WHERE [Id] = {1}", category, id);
    }

    [Theory]
    [InlineData("Patient")]            // case-sensitive
    [InlineData("traffic")]
    public async Task An_unknown_cancel_category_is_refused(string category)
    {
        var id = await AppointmentAsync();
        Constraint(await Refusal(() => SqlAsync("UPDATE [Appointments] SET [Status] = 'Cancelled', [CancelCategory] = {0} WHERE [Id] = {1}", category, id)), "CK_Appointments_CancelCategory");
    }

    [Fact]
    public async Task Only_a_cancelled_appointment_carries_a_cancel_category()
    {
        var id = await AppointmentAsync();
        Constraint(await Refusal(() => SqlAsync("UPDATE [Appointments] SET [CancelCategory] = 'patient' WHERE [Id] = {0}", id)), "CK_Appointments_CancelCategory");
    }

    [Fact]
    public async Task An_event_reason_is_never_blank()
    {
        var id = await AppointmentAsync();
        Constraint(await Refusal(() => AddAsync(new AppointmentEvent { Id = Guid.NewGuid(), AppointmentId = id, EventType = "Cancelled", OccurredAtUtc = DateTimeOffset.UtcNow, Reason = "   " })),
            "CK_AppointmentEvents_Reason");
        await AddAsync(new AppointmentEvent { Id = Guid.NewGuid(), AppointmentId = id, EventType = "Cancelled", OccurredAtUtc = DateTimeOffset.UtcNow, Reason = "Patient ill", Category = "patient" });
    }

    // ---------- M2 ----------

    [Fact]
    public async Task The_reassignment_kind_is_accepted_with_only_a_source_key()
    {
        await AddAsync(Reassigned());
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Kind == NotificationKinds.AppointmentProviderReassigned));
    }

    [Fact]
    public async Task The_reassignment_kind_is_refused_with_a_patient_or_without_a_source_key()
    {
        Constraint(await Refusal(() => AddAsync(Reassigned(n => n.RelatedPatientId = Ann))), "CK_Notifications_Shape");
        Constraint(await Refusal(() => AddAsync(Reassigned(n => n.SourceKey = null))), "CK_Notifications_Shape");
    }

    // ---------- M3 ----------

    public static TheoryData<string, string, string?, int?> ValidOutcomes => new()
    {
        { CheckoutItems.Departure, CheckoutOutcomes.PatientLeft, null, null },
        { CheckoutItems.FollowUp, CheckoutOutcomes.NotClinicallyRequired, null, null },
        { CheckoutItems.FollowUp, CheckoutOutcomes.PatientDeclined, null, null },
        { CheckoutItems.FollowUp, CheckoutOutcomes.Deferred, "Waiting for the specialist's letter", null },
        { CheckoutItems.Financial, CheckoutOutcomes.PaymentRecorded, null, 4 },
        { CheckoutItems.Financial, CheckoutOutcomes.Waived, null, 5 },
        { CheckoutItems.Financial, CheckoutOutcomes.PaymentPlan, null, null },
        { CheckoutItems.Financial, CheckoutOutcomes.InsuranceToBill, null, null },
        { CheckoutItems.Financial, CheckoutOutcomes.SendStatement, null, null },
        { CheckoutItems.Financial, CheckoutOutcomes.WaiverRequested, "Hardship; asked billing", null },
    };

    [Theory]
    [MemberData(nameof(ValidOutcomes))]
    public async Task Every_permitted_outcome_is_accepted(string item, string outcome, string? reason, int? ledger)
    {
        var id = await AppointmentAsync();
        await AddAsync(Outcome(id, item, outcome, o => { o.Reason = reason; o.LinkedLedgerEntryNumber = ledger; }));
        Assert.Equal(1, await OutcomeCountAsync());
    }

    [Theory]
    [InlineData("departure", "payment_plan")]            // an outcome of another item
    [InlineData("follow_up", "booked")]                  // booked is derived, never recorded
    [InlineData("financial", "Payment_Plan")]            // case-sensitive
    [InlineData("treatment", "patient_left")]            // treatment ended is derived, never recorded
    public async Task An_unknown_item_or_outcome_pair_is_refused(string item, string outcome)
    {
        var id = await AppointmentAsync();
        Constraint(await Refusal(() => AddAsync(Outcome(id, item, outcome))), "CK_VisitCheckoutOutcomes_Pair");
    }

    [Theory]
    [InlineData("deferred", null)]
    [InlineData("deferred", "  \t ")]
    [InlineData("waiver_requested", null)]
    public async Task A_reason_is_required_where_the_outcome_needs_one_and_is_never_blank(string outcome, string? reason)
    {
        var id = await AppointmentAsync();
        var item = outcome == "deferred" ? CheckoutItems.FollowUp : CheckoutItems.Financial;
        Constraint(await Refusal(() => AddAsync(Outcome(id, item, outcome, o => o.Reason = reason))), "CK_VisitCheckoutOutcomes_Reason");
    }

    [Theory]
    [InlineData("payment_recorded", null)]
    [InlineData("waived", 0)]
    [InlineData("payment_plan", 3)]
    public async Task A_ledger_entry_is_named_exactly_for_the_verified_financial_outcomes(string outcome, int? ledger)
    {
        var id = await AppointmentAsync();
        Constraint(await Refusal(() => AddAsync(Outcome(id, CheckoutItems.Financial, outcome, o => o.LinkedLedgerEntryNumber = ledger))), "CK_VisitCheckoutOutcomes_Ledger");
    }

    [Fact]
    public async Task A_linked_appointment_is_only_on_the_follow_up_item_and_never_the_visit_itself()
    {
        var id = await AppointmentAsync();
        var later = (await S.ScheduleAsync(S.Request(Ann, At(9, 0, dayOffset: 7)))).Appointment.Id;
        Constraint(await Refusal(() => AddAsync(Outcome(id, CheckoutItems.Departure, CheckoutOutcomes.PatientLeft, o => o.LinkedAppointmentId = later))), "CK_VisitCheckoutOutcomes_LinkedAppointment");
        Constraint(await Refusal(() => AddAsync(Outcome(id, CheckoutItems.FollowUp, CheckoutOutcomes.PatientDeclined, o => o.LinkedAppointmentId = id))), "CK_VisitCheckoutOutcomes_LinkedAppointment");
        await AddAsync(Outcome(id, CheckoutItems.FollowUp, CheckoutOutcomes.PatientDeclined, o => o.LinkedAppointmentId = later));
    }

    [Fact]
    public async Task An_outcome_is_never_updated_or_deleted()
    {
        var id = await AppointmentAsync();
        var o = Outcome(id, CheckoutItems.FollowUp, CheckoutOutcomes.PatientDeclined);
        await AddAsync(o);
        Assert.Equal(51282, (await Refusal(() => SqlAsync("UPDATE [VisitCheckoutOutcomes] SET [OutcomeCode] = 'deferred', [Reason] = N'x' WHERE [Id] = {0}", o.Id))).Number);
        Assert.Equal(51282, (await Refusal(() => SqlAsync("DELETE FROM [VisitCheckoutOutcomes] WHERE [Id] = {0}", o.Id))).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal(CheckoutOutcomes.PatientDeclined, (await db.VisitCheckoutOutcomes.SingleAsync()).OutcomeCode);
    }

    [Fact]
    public async Task A_correction_supersedes_an_earlier_outcome_of_the_same_visit_and_item_once_and_leaves_it_unchanged()
    {
        var id = await AppointmentAsync();
        var other = (await S.ScheduleAsync(S.Request(Bo, At(10)))).Appointment.Id;
        var first = Outcome(id, CheckoutItems.FollowUp, CheckoutOutcomes.PatientDeclined, o => o.RecordedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5));
        await AddAsync(first);

        Assert.Equal(51283, (await Refusal(() => AddAsync(Outcome(other, CheckoutItems.FollowUp, CheckoutOutcomes.NotClinicallyRequired, o => o.SupersedesId = first.Id)))).Number);  // another visit
        Assert.Equal(51283, (await Refusal(() => AddAsync(Outcome(id, CheckoutItems.Departure, CheckoutOutcomes.PatientLeft, o => o.SupersedesId = first.Id)))).Number);            // another item

        await AddAsync(Outcome(id, CheckoutItems.FollowUp, CheckoutOutcomes.NotClinicallyRequired, o => o.SupersedesId = first.Id));
        var twice = await Refusal(() => AddAsync(Outcome(id, CheckoutItems.FollowUp, CheckoutOutcomes.Deferred, o => { o.SupersedesId = first.Id; o.Reason = "later"; })));
        Assert.Contains("IX_VisitCheckoutOutcomes_SupersedesId", twice.Message);                                                                                                // superseded once only

        await using var db = Fixture.CreateContext();
        Assert.Equal(CheckoutOutcomes.PatientDeclined, (await db.VisitCheckoutOutcomes.SingleAsync(o => o.Id == first.Id)).OutcomeCode);                                   // the original is unchanged
    }

    [Fact]
    public async Task The_same_idempotency_key_from_the_same_actor_records_one_outcome()
    {
        var id = await AppointmentAsync();
        await AddAsync(Outcome(id, CheckoutItems.Departure, CheckoutOutcomes.PatientLeft, o => o.IdempotencyKey = "same-key"));
        var e = await Refusal(() => AddAsync(Outcome(id, CheckoutItems.Departure, CheckoutOutcomes.PatientLeft, o => o.IdempotencyKey = "same-key")));
        Assert.Contains("IX_VisitCheckoutOutcomes_ActorUserId_IdempotencyKey", e.Message);
        Assert.Equal(1, await OutcomeCountAsync());
    }

    // ---------- rollback and forward ----------

    [Fact]
    public async Task The_three_migrations_roll_back_and_forward_on_empty_tables()
    {
        await MigrateToAsync(BeforeM1);
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext()) Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await AddAsync(Outcome(await AppointmentAsync(), CheckoutItems.Departure, CheckoutOutcomes.PatientLeft));   // the restored schema works
    }

    [Fact]
    public async Task M3_refuses_to_roll_back_over_a_recorded_outcome()
    {
        await AddAsync(Outcome(await AppointmentAsync(), CheckoutItems.Departure, CheckoutOutcomes.PatientLeft));
        Assert.Equal(51284, (await Refusal(() => MigrateToAsync(BeforeM3))).Number);
        Assert.Equal(1, await OutcomeCountAsync());
    }

    [Fact]
    public async Task M2_refuses_to_roll_back_over_a_reassignment_notification()
    {
        await AddAsync(Reassigned());
        Assert.Equal(51281, (await Refusal(() => MigrateToAsync(BeforeM2))).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.Kind == NotificationKinds.AppointmentProviderReassigned));
    }

    [Fact]
    public async Task M1_refuses_to_roll_back_over_a_categorised_cancellation()
    {
        var id = await AppointmentAsync();
        await SqlAsync("UPDATE [Appointments] SET [Status] = 'Cancelled', [CancelCategory] = 'weather' WHERE [Id] = {0}", id);
        Assert.Equal(51280, (await Refusal(() => MigrateToAsync(BeforeM1))).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal("weather", (await db.Appointments.SingleAsync(a => a.Id == id)).CancelCategory);
    }
}
