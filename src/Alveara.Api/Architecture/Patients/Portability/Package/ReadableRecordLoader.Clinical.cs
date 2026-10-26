using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

public sealed partial class ReadableRecordLoader
{
    /// <summary>History, alerts, clearances, the dental chart and diagnoses.</summary>
    private async Task<List<ReadableSection>> ClinicalSectionsAsync(Guid patientId, CancellationToken ct)
    {
        var items = await db.ClinicalRecordItems.AsNoTracking().Where(i => i.PatientId == patientId).OrderBy(i => i.Kind).ThenBy(i => i.Name).ToListAsync(ct);
        var alerts = await db.SafetyAlerts.AsNoTracking().Where(a => a.PatientId == patientId).OrderBy(a => a.Category).ThenBy(a => a.Title).ToListAsync(ct);
        var clearances = await db.Clearances.AsNoTracking().Where(c => c.PatientId == patientId).OrderBy(c => c.RequestedAtUtc).ToListAsync(ct);
        var findings = await db.ToothFindings.AsNoTracking().Where(f => f.PatientId == patientId).OrderBy(f => f.ToothKey).ThenBy(f => f.Surface).ToListAsync(ct);
        var diagnoses = await db.Diagnoses.AsNoTracking().Where(d => d.PatientId == patientId).OrderBy(d => d.CreatedAtUtc).ToListAsync(ct);

        return
        [
            new("medical_history", "Medical history, medications and allergies", ["Type", "Name", "Details", "Status"],
                items.Select(i => Row(i.Kind, i.Name, Parts(i.Detail, i.Reaction, i.Severity, i.Dose, i.Frequency), i.RemovedAtUtc is null ? i.Status : $"{i.Status}, removed {Day(i.RemovedAtUtc)}")).ToList()),
            new("safety_alerts", "Safety alerts", ["Category", "Alert", "Severity", "Status", "Details"],
                alerts.Select(a => Row(a.Category, a.Title, a.Severity, a.ResolvedAtUtc is null ? a.Status : $"{a.Status}, resolved {Day(a.ResolvedAtUtc)}", Parts(a.Detail, a.ResolutionReason))).ToList()),
            new("clearances", "Clearances", ["Type", "Reason", "Requested from", "Requested", "Status"],
                clearances.Select(c => Row(c.Kind, c.Reason, c.RequestedFrom, Day(c.RequestedAtUtc), Parts(c.Status, c.ReceivedAtUtc is null ? null : $"received {Day(c.ReceivedAtUtc)}", c.ClosingReason))).ToList()),
            new("dental_chart", "Dental chart (tooth findings)", ["Tooth", "Surface", "Condition", "State", "Status"],
                findings.Select(f => Row(f.ToothKey, f.Surface, f.Condition, f.State, f.WithdrawnAtUtc is null ? f.Status : $"{f.Status}, withdrawn {Day(f.WithdrawnAtUtc)}")).ToList()),
            new("diagnoses", "Diagnoses", ["Recorded", "Diagnosis", "Tooth", "Code", "Status", "Notes"],
                diagnoses.Select(d => Row(Day(d.CreatedAtUtc), d.Label, d.ToothKey, Parts(d.CodingSystem, d.Code), d.WithdrawnAtUtc is null ? d.Status : $"{d.Status}, withdrawn {Day(d.WithdrawnAtUtc)}", Parts(d.Notes, d.WithdrawnReason))).ToList()),
        ];
    }

    /// <summary>Treatment plans with their items, what was completed, visits with their notes and vitals, and prescriptions.</summary>
    private async Task<List<ReadableSection>> PlanAndWorkSectionsAsync(Guid patientId, Dictionary<Guid, string> providers, CancellationToken ct)
    {
        var plans = await db.TreatmentPlans.AsNoTracking().Where(p => p.PatientId == patientId).OrderBy(p => p.CreatedAtUtc).ToListAsync(ct);
        var items = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PatientId == patientId).OrderBy(i => i.PlanId).ThenBy(i => i.ItemNumber).ToListAsync(ct);
        var states = (await db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.PatientId == patientId).ToListAsync(ct)).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Sequence).Last());
        var versions = await db.ProcedureVersions.AsNoTracking().Join(db.ProcedureDefinitions, v => v.ProcedureId, d => d.Id, (v, d) => new { v.Id, d.Code, v.Description }).ToDictionaryAsync(x => x.Id, x => $"{x.Code} {x.Description}", ct);
        var done = await db.CompletedProcedures.AsNoTracking().Where(c => c.PatientId == patientId).OrderBy(c => c.PerformedAtUtc).ToListAsync(ct);
        var encounters = await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId).OrderBy(e => e.EncounterAtUtc).ToListAsync(ct);
        var notes = (await db.EncounterNotes.AsNoTracking().Where(n => db.Encounters.Any(e => e.Id == n.EncounterId && e.PatientId == patientId)).OrderBy(n => n.Section).ToListAsync(ct)).ToLookup(n => n.EncounterId);
        var vitals = (await db.EncounterVitals.AsNoTracking().Where(v => v.PatientId == patientId && v.VoidedAtUtc == null).OrderBy(v => v.MeasuredAtUtc).ToListAsync(ct)).ToLookup(v => v.EncounterId);
        var prescriptions = await db.Prescriptions.AsNoTracking().Where(p => p.PatientId == patientId).OrderBy(p => p.CreatedAtUtc).ToListAsync(ct);
        var planTitles = plans.ToDictionary(p => p.Id, p => p.Title);
        var itemNumbers = items.ToDictionary(i => i.Id, i => $"{planTitles.GetValueOrDefault(i.PlanId, "")} #{i.ItemNumber}");

        string Progress(Architecture.Treatment.TreatmentPlanItem i) => i.WithdrawnAtUtc is not null ? $"Withdrawn {Day(i.WithdrawnAtUtc)}" :
            states.TryGetValue(i.Id, out var s) ? Parts($"Acceptance: {s.Acceptance}", $"Scheduling: {s.Scheduling}", $"Completion: {s.Completion}") : "";
        string Vitals(Guid encounterId) => string.Join("\n", vitals[encounterId].Select(v => "Vitals " + Parts(
            v.SystolicMmHg is null ? null : $"BP {v.SystolicMmHg}/{v.DiastolicMmHg}", v.PulseBpm is null ? null : $"pulse {v.PulseBpm}", v.TemperatureC is null ? null : $"{v.TemperatureC} C",
            v.OxygenSaturationPercent is null ? null : $"SpO2 {v.OxygenSaturationPercent}%", v.WeightKg is null ? null : $"{v.WeightKg} kg", v.Note)));

        return
        [
            new("treatment_plans", "Treatment plans", ["Plan", "Status", "Item", "Procedure", "Tooth", "Fee", "Progress"],
                items.Select(i => { var plan = plans.First(p => p.Id == i.PlanId); return Row($"{plan.Title}{(plan.ReferenceCode is null ? "" : $" ({plan.ReferenceCode})")}", plan.Status, i.ItemNumber.ToString(Inv), versions.GetValueOrDefault(i.ProcedureVersionId, ""), Tooth(i.ToothKey, i.Surface), Money(i.Fee), Progress(i)); }).ToList()),
            new("completed_procedures", "Completed procedures", ["Performed", "Procedure", "Plan item", "Tooth", "Provider", "Chart outcome"],
                done.Select(c => Row(Moment(c.PerformedAtUtc), versions.GetValueOrDefault(c.ProcedureVersionId, ""), itemNumbers.GetValueOrDefault(c.PlanItemId, ""), Tooth(c.ToothKey, c.Surface), Provider(providers, c.ProviderProfileId), c.ChartOutcome)).ToList()),
            new("visits", "Visits and clinical notes", ["Visit", "Status", "Notes and vitals"],
                encounters.Select(e => Row(Moment(e.EncounterAtUtc), e.SignedAtUtc is not null ? $"{e.Status}, signed {Day(e.SignedAtUtc)}" : e.Status,
                    string.Join("\n", notes[e.Id].Where(n => !string.IsNullOrWhiteSpace(n.Body)).Select(n => $"{n.Section}: {n.Body}").Append(Vitals(e.Id)).Where(s => s.Length > 0)))).ToList()),
            new("prescriptions", "Prescriptions", ["Prescribed", "Medication", "Dose", "Frequency", "Allergy check"],
                prescriptions.Select(p => Row(Day(p.CreatedAtUtc), p.Medication, $"{p.DoseValue.ToString("0.##", Inv)} {p.DoseUnit}", p.Frequency, Parts(p.AllergyOutcome, p.ReviewNote))).ToList()),
        ];
    }
}
