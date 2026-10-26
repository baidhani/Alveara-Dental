using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

public sealed partial class ReadableRecordLoader
{
    /// <summary>Appointments, billing, documents and the smaller parts of the record (periodontal sessions, forms, follow-ups, referrals and lab cases, recall).</summary>
    private async Task<List<ReadableSection>> AdministrativeSectionsAsync(Guid patientId, Dictionary<Guid, string> providers, CancellationToken ct)
    {
        var appointments = await db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId).OrderBy(a => a.StartUtc).ToListAsync(ct);
        var types = await db.AppointmentTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var operatories = await db.Operatories.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var entries = await db.BillingEntries.AsNoTracking().Where(e => e.PatientId == patientId).OrderBy(e => e.EntryNumber).ToListAsync(ct);
        var documents = await db.Documents.AsNoTracking().Where(d => d.PatientId == patientId).OrderBy(d => d.DocumentDate).ThenBy(d => d.ImportedAtUtc).ToListAsync(ct);
        var categories = await db.DocumentCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var perio = await db.PerioSessions.AsNoTracking().Where(s => s.PatientId == patientId).OrderBy(s => s.StartedAtUtc).ToListAsync(ct);
        var forms = await db.PatientForms.AsNoTracking().Where(f => f.PatientId == patientId).OrderBy(f => f.StartedAtUtc).ToListAsync(ct);
        var titles = await db.FormTemplateVersions.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.Title, ct);
        var tasks = await db.FollowUpTasks.AsNoTracking().Where(t => t.PatientId == patientId).OrderBy(t => t.DueAtUtc).ToListAsync(ct);
        var parties = await db.ExternalParties.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var referrals = await db.ReferralCases.AsNoTracking().Where(r => r.PatientId == patientId).OrderBy(r => r.CreatedAtUtc).ToListAsync(ct);
        var labs = await db.LabCases.AsNoTracking().Where(l => l.PatientId == patientId).OrderBy(l => l.CreatedAtUtc).ToListAsync(ct);
        var recall = await db.PatientRecallPlans.AsNoTracking().SingleOrDefaultAsync(r => r.PatientId == patientId, ct);

        // Totals per kind are shown, not a balance: what is owed depends on rules (reversals, refunds, releases) the billing screens apply, and a number computed here could disagree with them.
        var totals = string.Join("; ", entries.GroupBy(e => e.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}: {Money(g.Sum(e => e.Amount))} in {g.Count()} entr{(g.Count() == 1 ? "y" : "ies")}"));
        var billingNote = entries.Count == 0 ? null : $"Totals by kind (not a balance): {totals}.";

        return
        [
            new("appointments", "Appointments", ["Start", "Minutes", "Type", "Provider", "Room", "Status", "Notes"],
                appointments.Select(a => Row(Moment(a.StartUtc), a.DurationMinutes.ToString(Inv), types.GetValueOrDefault(a.AppointmentTypeId, ""), Provider(providers, a.ProviderProfileId), operatories.GetValueOrDefault(a.OperatoryId, ""), a.Status, Parts(a.Notes, a.CancelReason))).ToList()),
            new("billing", "Billing entries", ["No.", "Recorded", "Kind", "Amount", "Procedure", "Method", "Reason"],
                entries.Select(e => Row(e.EntryNumber.ToString(Inv), Day(e.RecordedAtUtc), e.Kind, Money(e.Amount), Parts(e.ProcedureCode, e.ProcedureDescription), e.PaymentMethod, e.Reason)).ToList(), billingNote),
            new("documents", "Documents", ["Date", "Document", "Category", "Type", "Size (bytes)", "Status", "SHA-256"],
                documents.Select(d => Row(Day(d.DocumentDate), Parts(d.Description, d.OriginalFileName), categories.GetValueOrDefault(d.CategoryId, ""), d.DetectedType, d.SizeBytes.ToString(Inv), d.Status, d.Sha256)).ToList(),
                "Documents are listed here. The files themselves are only in the complete export package."),
            new("periodontal", "Periodontal charting sessions", ["Started", "Status", "Closed"],
                perio.Select(s => Row(Moment(s.StartedAtUtc), s.Status, Day(s.ClosedAtUtc))).ToList()),
            new("forms", "Patient forms", ["Started", "Form", "Status"],
                forms.Select(f => Row(Day(f.StartedAtUtc), titles.GetValueOrDefault(f.TemplateVersionId, ""), f.VoidedAtUtc is null ? f.Status : $"{f.Status}, voided {Day(f.VoidedAtUtc)}")).ToList()),
            new("follow_ups", "Follow-up tasks", ["Due", "Task", "Kind", "Priority", "Status", "Note"],
                tasks.Select(t => Row(Day(t.DueAtUtc), t.Title, t.Kind, t.Priority, t.ClosedAtUtc is null ? t.Status : $"{t.Status}, closed {Day(t.ClosedAtUtc)}", Parts(t.Note, t.ClosureNote))).ToList()),
            new("referrals_and_labs", "Referrals and lab cases", ["Type", "With", "Subject", "Status", "Sent", "Received", "Notes"],
                referrals.Select(r => Row("Referral", parties.GetValueOrDefault(r.PartyId, ""), r.Reason, r.Status, Day(r.SentOn), Day(r.ReceivedOn), Parts(r.Notes, r.ClosureNote)))
                    .Concat(labs.Select(l => Row("Lab case", parties.GetValueOrDefault(l.PartyId, ""), Parts(l.Title, Tooth(l.ToothKey, null), l.Material, l.Shade), l.Status, Day(l.SentOn), Day(l.ReceivedOn), Parts(l.Notes, l.ClosureNote)))).ToList()),
            new("recall", "Recall plan", ["Interval (days)", "Opted out", "Reason", "Updated"],
                recall is null ? [] : [Row(recall.IntervalDays?.ToString(Inv), recall.OptedOut ? "Yes" : "No", recall.Reason, Day(recall.UpdatedAtUtc))]),
        ];
    }
}
