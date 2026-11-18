using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N023 (owner decisions DR-1, OQ-1; briefing revision 2 sections 2.1 S2 and 4): a real appointment books an accepted plan item. Linking writes one append-only link row, one plan event and one
/// audit row naming the procedure code, item number and plan reference (never the title); a repeat is quiet; the item shows the appointment live, so a move is followed and a cancellation or
/// no-show makes it not booked; refusals (not accepted, completed, withdrawn, another patient's or an ended appointment) store nothing; an unlink needs a reason; the result carries no clinical text;
/// and two links at the same moment store one row.
/// </summary>
public class TreatmentPlanAppointmentLinkTests : TreatmentPlanTestBase
{
    private async Task<(PlanView Plan, Guid Item)> AcceptedItemAsync(Guid? patient = null, string? code = null)
    {
        var p = patient ?? Ann;
        var proc = await ProcedureAsync(code: code ?? $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var plan = await CreateAsync([Item(await DiagnosisAsync(p), proc.Summary.Id)], patient: p);
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        return (plan, plan.Items[0].Id);
    }

    private Task<ItemAppointmentResult> Link(Guid plan, Guid item, Guid? appointment) => Svc(s => s.LinkAppointmentAsync(plan, item, appointment, S.Actor, default));
    private Task<ItemAppointmentResult> Unlink(Guid plan, Guid item, string? reason) => Svc(s => s.UnlinkAppointmentAsync(plan, item, reason, S.Actor, default));
    private async Task<int> LinkRowsAsync(Guid item) { await using var db = Fixture.CreateContext(); return await db.TreatmentPlanItemAppointmentLinks.CountAsync(l => l.ItemId == item); }

    [Fact]
    public async Task Linking_an_appointment_books_the_item_with_one_row_one_event_and_a_PHI_free_audit_entry()
    {
        var (plan, item) = await AcceptedItemAsync(code: "LOCAL-CROWN1");
        var appointment = await AppointmentAsync();
        var result = await Link(plan.Id, item, appointment);

        Assert.Equal((plan.Id, item, appointment, true, AppointmentStatuses.Scheduled), (result.PlanId, result.ItemId, result.Appointment!.AppointmentId, result.Appointment.Live, result.Appointment.Status));
        var view = await Svc(s => s.GetAsync(plan.Id, default));
        Assert.Equal(appointment, view.Items[0].Appointment!.AppointmentId);
        Assert.Equal(ItemAcceptance.Accepted, view.Items[0].Acceptance);                                    // the link never changes the answer
        await using var db = Fixture.CreateContext();
        var row = await db.TreatmentPlanItemAppointmentLinks.AsNoTracking().SingleAsync(l => l.ItemId == item);
        Assert.Equal((1, AppointmentLinkActions.Linked, (string?)null, Ann), (row.Sequence, row.Action, row.Reason, row.PatientId));
        Assert.Equal(1, await db.TreatmentPlanEvents.CountAsync(e => e.PlanId == plan.Id && e.ChangeType == TreatmentPlanChanges.AppointmentLinked && e.ItemId == item));
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "TreatmentPlanItemAppointmentLinked");
        Assert.Contains("LOCAL-CROWN1", audit.Details);
        Assert.Contains("item 1", audit.Details);
        Assert.DoesNotContain(plan.Title, audit.Details);
    }

    [Fact]
    public async Task Linking_the_same_appointment_again_is_quiet()
    {
        var (plan, item) = await AcceptedItemAsync();
        var appointment = await AppointmentAsync();
        await Link(plan.Id, item, appointment);
        await Link(plan.Id, item, appointment);
        Assert.Equal(1, await LinkRowsAsync(item));
    }

    [Fact]
    public async Task The_item_follows_a_move_and_is_no_longer_booked_once_the_appointment_is_cancelled_or_a_no_show()
    {
        var (plan, item) = await AcceptedItemAsync();
        var appointment = await AppointmentAsync();
        await Link(plan.Id, item, appointment);
        var moved = new DateTimeOffset(2030, 2, 6, 16, 0, 0, TimeSpan.Zero);
        await MoveAppointmentAsync(appointment, moved);
        Assert.Equal(moved, (await Svc(s => s.GetAsync(plan.Id, default))).Items[0].Appointment!.StartUtc);   // no plan write needed

        await EndAppointmentAsync(appointment, AppointmentStatuses.Cancelled);
        var view = await Svc(s => s.GetAsync(plan.Id, default));
        Assert.Equal((false, AppointmentStatuses.Cancelled), (view.Items[0].Appointment!.Live, view.Items[0].Appointment!.Status));

        var (plan2, item2) = await AcceptedItemAsync();
        var second = await AppointmentAsync();
        await Link(plan2.Id, item2, second);
        await EndAppointmentAsync(second, AppointmentStatuses.NoShow);
        Assert.False((await Svc(s => s.GetAsync(plan2.Id, default))).Items[0].Appointment!.Live);
    }

    [Theory]
    [InlineData("pending", "item_not_accepted")]
    [InlineData("declined", "item_not_accepted")]
    [InlineData("withdrawn item", "item_withdrawn")]
    [InlineData("withdrawn plan", "plan_withdrawn")]
    public async Task An_item_that_is_not_an_open_accepted_procedure_cannot_be_booked_and_nothing_is_stored(string situation, string code)
    {
        var proc = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var plan = await CreateAsync([Item(await DiagnosisAsync(), proc.Summary.Id)]);
        var item = plan.Items[0].Id;
        switch (situation)
        {
            case "declined": await Svc(s => s.DecideItemAsync(plan.Id, item, "Declined", "Too costly", null, plan.RowVersion, S.Actor, default)); break;
            case "withdrawn item": await Svc(s => s.WithdrawItemAsync(plan.Id, item, "Wrong tooth", plan.RowVersion, S.Actor, default)); break;
            case "withdrawn plan": await Svc(s => s.WithdrawAsync(plan.Id, "Wrong patient", plan.RowVersion, S.Actor, default)); break;
        }
        var appointment = await AppointmentAsync();
        var ex = await Assert.ThrowsAsync<TreatmentPlanException>(() => Link(plan.Id, item, appointment));
        Assert.Equal(code, ex.Code);
        Assert.Equal(0, await LinkRowsAsync(item));
    }

    [Fact]
    public async Task A_completed_procedure_cannot_be_booked()
    {
        var dx = await DiagnosisAsync();
        var p1 = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var p2 = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var plan = await CreateAsync([Item(dx, p1.Summary.Id), Item(dx, p2.Summary.Id)]);                                // a second, unanswered procedure keeps the plan open
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        var item = plan.Items[0].Id;
        await LegacyComplete(plan.Id, item);
        var ex = await Assert.ThrowsAsync<TreatmentPlanException>(async () => await Link(plan.Id, item, await AppointmentAsync()));
        Assert.Equal("item_completed", ex.Code);
    }

    [Fact]
    public async Task Another_patients_appointment_an_unknown_one_and_an_ended_one_are_refused()
    {
        var (plan, item) = await AcceptedItemAsync();
        var bo = await AppointmentAsync(Bo);
        Assert.Equal("appointmentId", Assert.Single((await Assert.ThrowsAsync<TreatmentPlanException>(() => Link(plan.Id, item, bo))).FieldErrors).Key);
        Assert.Equal("appointmentId", Assert.Single((await Assert.ThrowsAsync<TreatmentPlanException>(() => Link(plan.Id, item, Guid.NewGuid()))).FieldErrors).Key);
        Assert.Equal("appointmentId", Assert.Single((await Assert.ThrowsAsync<TreatmentPlanException>(() => Link(plan.Id, item, null))).FieldErrors).Key);
        var cancelled = await AppointmentAsync();
        await EndAppointmentAsync(cancelled, AppointmentStatuses.Cancelled);
        Assert.Equal("appointment_not_active", (await Assert.ThrowsAsync<TreatmentPlanException>(() => Link(plan.Id, item, cancelled))).Code);
        Assert.Equal(0, await LinkRowsAsync(item));
    }

    [Fact]
    public async Task Unlinking_needs_a_reason_records_it_and_unlinking_nothing_is_quiet()
    {
        var (plan, item) = await AcceptedItemAsync();
        await Unlink(plan.Id, item, "Not needed");                                                              // nothing linked: quiet
        Assert.Equal(0, await LinkRowsAsync(item));
        var appointment = await AppointmentAsync();
        await Link(plan.Id, item, appointment);
        Assert.Equal("reason_required", (await Assert.ThrowsAsync<TreatmentPlanException>(() => Unlink(plan.Id, item, "  "))).Code);
        var result = await Unlink(plan.Id, item, "Patient will call back");
        Assert.Null(result.Appointment);
        await using var db = Fixture.CreateContext();
        var row = await db.TreatmentPlanItemAppointmentLinks.AsNoTracking().Where(l => l.ItemId == item).OrderByDescending(l => l.Sequence).FirstAsync();
        Assert.Equal((2, AppointmentLinkActions.Unlinked, "Patient will call back", appointment), (row.Sequence, row.Action, row.Reason, row.AppointmentId));
        Assert.Equal("Patient will call back", (await db.TreatmentPlanEvents.AsNoTracking().SingleAsync(e => e.ChangeType == TreatmentPlanChanges.AppointmentUnlinked)).Reason);
        Assert.Null((await Svc(s => s.GetAsync(plan.Id, default))).Items[0].Appointment);
    }

    [Fact]
    public async Task Relinking_to_a_later_appointment_appends_a_row_and_books_the_new_one()
    {
        var (plan, item) = await AcceptedItemAsync();
        var first = await AppointmentAsync();
        var later = await AppointmentAsync(start: new DateTimeOffset(2030, 3, 1, 15, 0, 0, TimeSpan.Zero));
        await Link(plan.Id, item, first);
        var result = await Link(plan.Id, item, later);
        Assert.Equal(later, result.Appointment!.AppointmentId);
        Assert.Equal(2, await LinkRowsAsync(item));
    }

    [Fact]
    public void The_link_result_carries_no_plan_title_diagnosis_or_other_clinical_text()
    {
        var fields = typeof(ItemAppointmentResult).GetProperties().Select(p => p.Name).Concat(typeof(ItemAppointmentView).GetProperties().Select(p => p.Name)).ToList();
        Assert.Equal(new[] { "PlanId", "ItemId", "Appointment", "AppointmentId", "StartUtc", "ProviderName", "Status", "Live", "LinkedByName", "LinkedAtUtc" }, fields);
    }

    [Fact]
    public async Task Two_links_of_the_same_item_at_the_same_moment_store_one_row()
    {
        var (plan, item) = await AcceptedItemAsync();
        var appointment = await AppointmentAsync();
        var results = await Task.WhenAll(Link(plan.Id, item, appointment), Link(plan.Id, item, appointment));
        Assert.All(results, r => Assert.Equal(appointment, r.Appointment!.AppointmentId));
        Assert.Equal(1, await LinkRowsAsync(item));
    }
}
