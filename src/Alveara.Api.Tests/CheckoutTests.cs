using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Scheduling.Checkout;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 step 2 against real SQL Server: the checkout evaluator and recorder (OD-22 to OD-31), before completion is wired to them. Which items apply, what resolves them, who may record which
/// outcome, the read-only ledger verification of "payment recorded" and "waived", corrections as new rows, retries, and that no checkout action ever changes the ledger.
/// </summary>
public class CheckoutTests : CompletionTestBase
{
    /// <summary>The code default matrix as the effective policy (no stored overrides), so the recorder's permission checks are the real defaults.</summary>
    private sealed class Defaults : IEffectivePermissions
    {
        public Task<bool> HasAsync(Role role, Permission permission, CancellationToken ct = default) => Task.FromResult(PermissionMatrix.RoleHas(role, permission));
        public Task<PermissionOutcome> CheckAsync(Role role, Permission permission, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PermissionOutcome> CheckAnyAsync(Role role, IReadOnlyCollection<Permission> permissions, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<Permission>> PermissionsForAsync(Role role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Role, IReadOnlySet<Permission>>> MatrixAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PolicyRead> SnapshotAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private Task<CheckoutReadiness> EvaluateAsync(Guid visit) => WithDb(db => new CheckoutEvaluator(db, Clock).EvaluateAsync(visit, default));

    private Task<CheckoutReadiness> RecordAsync(Guid visit, string item, string outcome, string? reason = null, int? ledger = null, Role role = Role.FrontDesk, Guid? actor = null, string? key = null) =>
        WithDb(db => new CheckoutRecorder(db, Clock, new Defaults()).RecordAsync(visit, item, new RecordCheckoutOutcomeRequest(outcome, reason, ledger), actor ?? S.Actor, role, key ?? Guid.NewGuid().ToString("N"), default));

    private static async Task<CheckoutException> Refused(Func<Task> action) => await Assert.ThrowsAsync<CheckoutException>(action);

    private static CheckoutItemView Item(CheckoutReadiness r, string code) => r.Items.Single(i => i.Code == code);

    /// <summary>A visit of Ann's walked along the chain to <paramref name="until"/> (Ready for checkout by default).</summary>
    private async Task<AppointmentView> VisitAsync(string until = VisitStates.CheckedOut, int hour = 9, Guid? patient = null)
    {
        var a = (await S.ScheduleAsync(S.Request(patient ?? Ann, At(hour)))).Appointment;
        foreach (var step in VisitStates.InOrder.Skip(1))
        {
            if (a.FlowState == until) break;
            a = await S.FlowAsync(f => f.TransitionAsync(a.Id, step, a.RowVersion, S.Actor, default));
        }
        return a;
    }

    private async Task<(int Entries, int Allocations)> LedgerCountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.BillingEntries.CountAsync(), await db.BillingAllocations.CountAsync());
    }

    private static DateTimeOffset VisitInstant(AppointmentView v) => v.StartUtc;

    // ---------- items and applicability ----------

    [Fact]
    public async Task Before_treatment_ends_checkout_is_closed_and_says_so()
    {
        var v = await VisitAsync(VisitStates.InTreatment);
        var r = await EvaluateAsync(v.Id);
        Assert.False(r.Open);
        Assert.False(r.Ready);
        Assert.Equal("Checkout opens when treatment ends.", r.Summary);
        Assert.False(Item(r, CheckoutItemCodes.TreatmentEnded).Resolved);
        Assert.Equal("checkout_not_open", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Departure, CheckoutOutcomes.PatientLeft))).Code);
    }

    [Fact]
    public async Task With_nothing_owed_the_financial_item_does_not_apply_and_departure_and_follow_up_are_open()
    {
        var v = await VisitAsync();
        var r = await EvaluateAsync(v.Id);
        Assert.True(r.Open);
        Assert.True(Item(r, CheckoutItemCodes.TreatmentEnded).Resolved);
        Assert.False(Item(r, CheckoutItems.Financial).Applies);
        Assert.Equal([CheckoutItems.Departure, CheckoutItems.FollowUp], r.OpenItemCodes);
        Assert.Equal("Ready for checkout — patient departure not recorded (and 1 more).", r.Summary);
        Assert.Equal("Front desk", Item(r, CheckoutItems.Departure).ResponsibleRole);
    }

    [Fact]
    public async Task Recording_departure_and_a_follow_up_decision_makes_the_visit_ready_and_never_touches_the_ledger()
    {
        var v = await VisitAsync();
        var before = await LedgerCountsAsync();
        await RecordAsync(v.Id, CheckoutItems.Departure, CheckoutOutcomes.PatientLeft);
        var r = await RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.PatientDeclined);
        Assert.True(r.Ready);
        Assert.Empty(r.OpenItemCodes);
        Assert.Equal("Ready for checkout — every item is resolved; the visit can be completed.", r.Summary);
        Assert.Equal(before, await LedgerCountsAsync());
        Assert.NotNull(Item(r, CheckoutItems.Departure).Outcome!.RecordedByName);                                 // who recorded it is shown
    }

    [Fact]
    public async Task A_real_future_appointment_books_the_follow_up_and_booked_cannot_be_recorded_by_hand()
    {
        var v = await VisitAsync();
        Assert.Equal("validation_failed", (await Refused(() => RecordAsync(v.Id, CheckoutItems.FollowUp, DerivedCheckoutOutcomes.FollowUpBooked))).Code);
        var next = (await S.ScheduleAsync(S.Request(Ann, At(9, 0, dayOffset: 30)))).Appointment;
        var f = Item(await EvaluateAsync(v.Id), CheckoutItems.FollowUp);
        Assert.True(f.Resolved);
        Assert.Equal((DerivedCheckoutOutcomes.FollowUpBooked, true, (Guid?)next.Id), (f.Outcome!.Code, f.Outcome.Derived, f.Outcome.LinkedAppointmentId));
    }

    [Fact]
    public async Task A_balance_owed_makes_the_financial_item_apply_and_a_balance_paid_in_full_does_not()
    {
        // the ledger itself refuses an entry that would leave a negative balance, so a credit balance cannot arise; "paid in full" is the reachable state below zero-owed
        var v = await VisitAsync();
        var account = await NewAccountAsync();
        var plan = await BillablePlanAsync();
        await AddRowAsync(ChargeRow(account, plan.Completed, 100m, 1, "c1"));
        Assert.True(Item(await EvaluateAsync(v.Id), CheckoutItems.Financial).Applies);
        await AddRowAsync(PaymentRow(account, -100m, 2, "p1"));
        Assert.False(Item(await EvaluateAsync(v.Id), CheckoutItems.Financial).Applies);
    }

    [Fact]
    public async Task A_completed_procedure_of_this_visit_with_no_live_charge_makes_the_financial_item_apply()
    {
        var v = await VisitAsync();
        var provider = await ProviderAsync();
        var plan = await AcceptedPlanAsync(1);
        await Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [0], AnnEncounter, provider.Id, "set-1", DateTimeOffset.UtcNow.AddSeconds(2)), S.Actor, default));   // completes and charges
        await WithDb(async db =>
        {
            var account = await db.BillingAccounts.SingleAsync(x => x.PatientId == Ann);
            var charge = await db.BillingEntries.SingleAsync(e => e.PatientId == Ann && e.Kind == BillingKinds.Charge);
            await AddRowAsync(ReversalRow(account, charge, 2, "undo-charge"));                                   // balance back to 0, the procedure has no live charge
            return 0;
        });
        Assert.False(Item(await EvaluateAsync(v.Id), CheckoutItems.Financial).Applies);                          // its encounter is not this visit's yet
        await WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE [Encounters] SET [AppointmentId] = {0} WHERE [Id] = {1}", v.Id, AnnEncounter));
        var r = await EvaluateAsync(v.Id);
        Assert.True(Item(r, CheckoutItems.Financial).Applies);
        Assert.Contains(CheckoutItems.Financial, r.OpenItemCodes);
    }

    // ---------- verified financial outcomes (ledger read only) ----------

    [Fact]
    public async Task Payment_recorded_resolves_only_with_a_live_payment_of_the_visit_day_and_reopens_if_it_is_reversed()
    {
        var v = await VisitAsync();
        var account = await NewAccountAsync();
        var plan = await BillablePlanAsync();
        await AddRowAsync(ChargeRow(account, plan.Completed, 100m, 1, "c1"));
        var dayBefore = PaymentRow(account, -10m, 2, "p-early");
        await AddRowAsync(dayBefore, e => e.RecordedAtUtc = VisitInstant(v).AddDays(-1));
        var sameDay = PaymentRow(account, -40m, 3, "p-day");
        await AddRowAsync(sameDay, e => e.RecordedAtUtc = VisitInstant(v).AddHours(2));
        var before = await LedgerCountsAsync();

        Assert.Equal("validation_failed", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.PaymentRecorded))).Code);              // no entry named
        Assert.Equal("checkout_outcome_unverified", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.PaymentRecorded, ledger: 99))).Code);  // no such entry
        Assert.Equal("checkout_outcome_unverified", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.PaymentRecorded, ledger: 1))).Code);   // a charge
        Assert.Equal("checkout_outcome_unverified", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.PaymentRecorded, ledger: 2))).Code);   // another day

        var r = await RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.PaymentRecorded, ledger: 3);
        Assert.True(Item(r, CheckoutItems.Financial).Resolved);
        Assert.Equal(before, await LedgerCountsAsync());                                                         // the checkout wrote no ledger row

        await AddRowAsync(ReversalRow(account, sameDay, 4, "r1"));
        var reopened = Item(await EvaluateAsync(v.Id), CheckoutItems.Financial);
        Assert.False(reopened.Resolved);
        Assert.StartsWith("The ledger entry this relied on was reversed", reopened.NextAction);
        Assert.Equal("checkout_outcome_unverified", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.PaymentRecorded, ledger: 3))).Code);
    }

    [Fact]
    public async Task Waived_needs_a_negative_adjustment_of_the_visit_day()
    {
        var v = await VisitAsync();
        var account = await NewAccountAsync();
        var plan = await BillablePlanAsync();
        await AddRowAsync(ChargeRow(account, plan.Completed, 100m, 1, "c1"));
        await AddRowAsync(AdjustmentRow(account, 5m, 2, "a-up"), e => e.RecordedAtUtc = VisitInstant(v));
        await AddRowAsync(AdjustmentRow(account, -100m, 3, "a-down"), e => e.RecordedAtUtc = VisitInstant(v));
        Assert.Equal("checkout_outcome_unverified", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.Waived, ledger: 2))).Code);
        Assert.True(Item(await RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.Waived, ledger: 3, role: Role.Billing), CheckoutItems.Financial).Resolved);
    }

    [Fact]
    public async Task Waiver_requested_needs_a_reason_and_never_resolves_the_financial_item()
    {
        var v = await VisitAsync();
        var account = await NewAccountAsync();
        var plan = await BillablePlanAsync();
        await AddRowAsync(ChargeRow(account, plan.Completed, 100m, 1, "c1"));
        Assert.Equal("reason_required", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.WaiverRequested, reason: "  "))).Code);
        var f = Item(await RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.WaiverRequested, reason: "Hardship"), CheckoutItems.Financial);
        Assert.False(f.Resolved);
        Assert.StartsWith("Waiver requested", f.NextAction);
        Assert.True(Item(await RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.SendStatement), CheckoutItems.Financial).Resolved);   // another decision resolves it
    }

    // ---------- who may record ----------

    [Theory]
    [InlineData(Role.Assistant, "departure", "patient_left")]
    [InlineData(Role.Billing, "departure", "patient_left")]
    [InlineData(Role.Dentist, "follow_up", "patient_declined")]
    [InlineData(Role.Hygienist, "financial", "payment_plan")]
    [InlineData(Role.Dentist, "financial", "insurance_to_bill")]
    [InlineData(Role.FrontDesk, "follow_up", "not_clinically_required")]
    [InlineData(Role.Unassigned, "departure", "patient_left")]
    public async Task A_role_without_the_duty_is_refused_and_nothing_is_written(Role role, string item, string outcome)
    {
        var v = await VisitAsync();
        Assert.Equal(403, (await Refused(() => RecordAsync(v.Id, item, outcome, role: role))).StatusCode);
        await using var db = Fixture.CreateContext();
        Assert.Equal(0, await db.VisitCheckoutOutcomes.CountAsync());
    }

    [Fact]
    public async Task Only_the_treating_provider_records_that_no_follow_up_is_clinically_required()
    {
        var v = await VisitAsync();
        var treating = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.UserAccounts.Add(new UserAccount { Id = treating, Username = $"rivera-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return await db.Database.ExecuteSqlRawAsync("UPDATE s SET [UserAccountId] = {0} FROM [StaffProfiles] s JOIN [ProviderProfiles] p ON p.[StaffProfileId] = s.[Id] WHERE p.[Id] = {1}", treating, S.ProviderA);
        });
        Assert.Equal(403, (await Refused(() => RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.NotClinicallyRequired, role: Role.Dentist))).StatusCode);   // a dentist, not this visit's
        Assert.Equal(403, (await Refused(() => RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.NotClinicallyRequired, role: Role.Assistant, actor: treating))).StatusCode);
        Assert.True(Item(await RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.NotClinicallyRequired, role: Role.Dentist, actor: treating), CheckoutItems.FollowUp).Resolved);
    }

    // ---------- corrections, history and retries ----------

    [Fact]
    public async Task A_correction_is_a_new_row_that_supersedes_the_old_one_with_history_and_audit()
    {
        var v = await VisitAsync();
        await RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.PatientDeclined);
        var r = await RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.Deferred, reason: "Waiting for the specialist's letter");
        Assert.Equal(CheckoutOutcomes.Deferred, Item(r, CheckoutItems.FollowUp).Outcome!.Code);
        await using var db = Fixture.CreateContext();
        var rows = await db.VisitCheckoutOutcomes.OrderBy(o => o.RecordedAtUtc).ThenBy(o => o.SupersedesId != null).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal((CheckoutOutcomes.PatientDeclined, (Guid?)null), (rows[0].OutcomeCode, rows[0].SupersedesId));                              // the original is unchanged
        Assert.Equal(rows[0].Id, rows[1].SupersedesId);
        var events = await db.AppointmentEvents.Where(e => e.AppointmentId == v.Id && e.EventType == CheckoutRecorder.HistoryEvent).OrderBy(e => e.OccurredAtUtc).ToListAsync();
        Assert.Equal(["follow_up: patient_declined", "follow_up: patient_declined -> deferred"], events.Select(e => e.Detail));
        Assert.Equal(2, await db.AuditLogEntries.CountAsync(e => e.EventType == CheckoutRecorder.AuditEvent && e.TargetUserAccountId == v.Id));
        Assert.DoesNotContain(await db.AuditLogEntries.Where(e => e.EventType == CheckoutRecorder.AuditEvent).Select(e => e.Details).ToListAsync(), d => d.Contains("specialist"));   // reasons stay out of the audit log
    }

    [Fact]
    public async Task Recording_what_the_item_already_has_writes_nothing()
    {
        var v = await VisitAsync();
        await RecordAsync(v.Id, CheckoutItems.Departure, CheckoutOutcomes.PatientLeft);
        await RecordAsync(v.Id, CheckoutItems.Departure, CheckoutOutcomes.PatientLeft);
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.VisitCheckoutOutcomes.CountAsync());
    }

    [Fact]
    public async Task A_retry_with_the_same_key_records_once_and_a_different_request_with_that_key_is_refused()
    {
        var v = await VisitAsync();
        await RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.PatientDeclined, key: "k1");
        await RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.PatientDeclined, key: "k1");
        Assert.Equal("idempotency_conflict", (await Refused(() => RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.Deferred, reason: "x", key: "k1"))).Code);
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.VisitCheckoutOutcomes.CountAsync());
    }

    [Fact]
    public async Task Bad_input_is_refused_before_anything_is_read_or_written()
    {
        var v = await VisitAsync();
        Assert.Equal("checkout_item_unknown", (await Refused(() => RecordAsync(v.Id, "treatment_ended", CheckoutOutcomes.PatientLeft))).Code);
        Assert.Equal("validation_failed", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Departure, CheckoutOutcomes.PaymentPlan))).Code);
        Assert.Equal("validation_failed", (await Refused(() => RecordAsync(v.Id, CheckoutItems.Financial, CheckoutOutcomes.PaymentPlan, ledger: 3))).Code);
        Assert.Equal("validation_failed", (await Refused(() => RecordAsync(v.Id, CheckoutItems.FollowUp, CheckoutOutcomes.Deferred, reason: new string('x', 501)))).Code);
        Assert.Equal("appointment_not_found", (await Refused(() => RecordAsync(Guid.NewGuid(), CheckoutItems.Departure, CheckoutOutcomes.PatientLeft))).Code);
        Assert.Equal("idempotency_key_required", (await Refused(() => WithDb(db =>
            new CheckoutRecorder(db, Clock, new Defaults()).RecordAsync(v.Id, CheckoutItems.Departure, new RecordCheckoutOutcomeRequest(CheckoutOutcomes.PatientLeft, null, null), S.Actor, Role.FrontDesk, " ", default)))).Code);
    }
}
