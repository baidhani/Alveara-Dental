using System.Globalization;
using System.Text.Json;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// ALV-N026 S7: the input for the <c>treatment-plan-copy/v2</c> template, with the same words as the patient copy on screen (ALV-015-C01, ALV-N023 F-183) plus the MRN. Withdrawn procedures are left out;
/// staff-only text (clinical rationale, who entered what, history) is never included. Dates are practice-local; the date printed is the issue's, so a reprint keeps it.
/// </summary>
public static class TreatmentPlanCopyPdf
{
    public const string TemplateId = "treatment-plan-copy";
    public const string EstimateNote =
        "This is the practice's own estimate from its fee schedule. It does not include any insurance benefits, and it is not a quote of what you will be asked to pay. Ask the practice if you have questions.";

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public sealed record Request(string TemplateId, string TemplateVersion, string InputJson, string FileName);

    public static string Fee(decimal amount) => amount.ToString("C2", Us);

    public static string AnswerWords(PlanCopyLine l)
    {
        var parts = new List<string> { l.Acceptance == ItemAcceptance.Accepted ? "Your answer: accepted" : l.Acceptance == ItemAcceptance.Declined ? "Your answer: declined" : "Your answer: not yet given" };
        if (l.Scheduling == ItemScheduling.Scheduled) parts.Add(l.PlannedDate is { } d ? $"planned for {d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : "planned");
        if (l.Completion == ItemCompletion.Completed) parts.Add("Completed");
        return string.Join(" · ", parts);
    }

    public static Request For(TreatmentPlanCopyIssue issue, PlanCopyContent c, IPracticeClock clock, DateTimeOffset producedAtUtc)
    {
        string Minute(DateTimeOffset utc) => clock.ToPracticeLocal(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var anyDone = c.Lines.Any(l => l.Completion == ItemCompletion.Completed);
        object Line(PlanCopyLine l) => new
        {
            description = l.Description,
            site = l.ToothKey is null ? null : $"Tooth {ToothNumbering.Display(l.ToothKey, NumberingSystem.Universal)}{(l.Surface is null ? "" : $", surface {l.Surface}")}",
            diagnosis = l.DiagnosisLabel,
            fee = Fee(l.Fee),
            answer = AnswerWords(l),
        };
        var phaseNumbers = c.Phases.Select(p => p.PhaseNumber).ToHashSet();
        var groups = c.Phases.Select(p => new
        {
            title = $"Phase {p.PhaseNumber}: {p.Name}",
            when = string.Join(" · ", new[] { p.PlannedVisit, p.TargetDate is { } t ? "target " + t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null }.Where(x => !string.IsNullOrWhiteSpace(x))),
            items = c.Lines.Where(l => l.PhaseNumber == p.PhaseNumber).Select(Line).ToList(),
        }).ToList();
        var loose = c.Lines.Where(l => l.PhaseNumber is null || !phaseNumbers.Contains(l.PhaseNumber.Value)).ToList();
        if (c.Phases.Count == 0) groups = [new { title = "", when = "", items = c.Lines.Select(Line).ToList() }];
        else if (loose.Count > 0) groups.Add(new { title = "To be planned", when = "", items = loose.Select(Line).ToList() });
        var accepted = c.Lines.Where(l => l.Acceptance == ItemAcceptance.Accepted).Sum(l => l.Fee);
        var issued = Minute(issue.IssuedAtUtc);
        var input = new
        {
            templateVersion = issue.TemplateVersion,
            issueId = issue.Id.ToString("N")[..8],
            issued,
            produced = Minute(producedAtUtc),
            practice = c.Envelope.Practice.Name is null ? null : c.Envelope.Practice,
            title = $"Your treatment plan: {c.Title}",
            prepared = string.IsNullOrWhiteSpace(c.Envelope.PatientName) ? $"Prepared on {issued[..10]}." : $"Prepared for {c.Envelope.PatientName} on {issued[..10]}.",
            mrn = c.Envelope.Mrn.ToString(CultureInfo.InvariantCulture),
            explanation = c.PatientExplanation ?? (anyDone
                ? "This is the care your dentist planned with you, including what has already been done. Please ask if anything is unclear."
                : "Your dentist has proposed the care below. Please ask if anything is unclear."),
            groups,
            total = Fee(c.Lines.Sum(l => l.Fee)),
            accepted = accepted > 0 ? Fee(accepted) : null,
            estimateNote = EstimateNote,
            foot = $"Plan version {c.RevisionNumber}{(c.ReferenceCode is null ? "" : $" · Plan reference: {c.ReferenceCode}")}",
        };
        return new Request(TemplateId, issue.TemplateVersion, JsonSerializer.Serialize(input, Json), $"Treatment-plan-{issue.Id.ToString("N")[..8]}.pdf");
    }
}
