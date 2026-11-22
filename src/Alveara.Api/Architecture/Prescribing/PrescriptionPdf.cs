using System.Globalization;
using System.Text.Json;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>
/// ALV-N026 S6: the input for the <c>prescription/p2</c> template. The medication is printed exactly as recorded (no strength or dosage-form labels); dates are practice-local; an inactive
/// status gives the watermark with its date and reason. The boundary statement is printed on every p2. No DEA field and no controlled-substance or compliance claim exist here.
/// </summary>
public static class PrescriptionPdf
{
    public const string Boundary =
        "General U.S.-oriented prescription layout produced by DentalKeep. It is not a federal or state compliance profile, and is not designed or validated as a controlled-substance prescription form. " +
        "DentalKeep does not supply or verify tamper-resistant paper. DentalKeep does not determine whether a medication is a controlled substance. The prescriber is responsible for meeting the " +
        "requirements of their jurisdiction, payer and dispensing pharmacy.";

    /// <summary>F-165: the plural of a quantity unit, from a small fixed table; a unit not in it (mL, g) is unchanged.</summary>
    private static readonly IReadOnlyDictionary<string, string> Plurals = new Dictionary<string, string>
    {
        ["tablet"] = "tablets", ["capsule"] = "capsules", ["tube"] = "tubes", ["bottle"] = "bottles", ["inhaler"] = "inhalers", ["unit"] = "units",
    };

    /// <summary>Q-5am: display labels for stored frequency values; the stored values are unchanged.</summary>
    public static string FrequencyLabel(string stored) => stored == "Once" ? "Once only (single dose)" : stored;

    public static string Quantity(decimal value, string unit) =>
        $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {(value != 1m && Plurals.TryGetValue(unit, out var plural) ? plural : unit)}";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public sealed record Request(string TemplateId, string TemplateVersion, string InputJson, string FileName);

    public static Request For(PrescriptionIssue issue, PrescriptionRecord r, PrescriptionIssueEnvelope envelope, DateTimeOffset? endedAtUtc, string? endedReason, IPracticeClock clock, DateTimeOffset producedAtUtc)
    {
        var inv = CultureInfo.InvariantCulture;
        string Day(DateTimeOffset utc) => clock.ToPracticeLocal(utc).ToString("yyyy-MM-dd", inv);
        string Minute(DateTimeOffset utc) => clock.ToPracticeLocal(utc).ToString("yyyy-MM-dd HH:mm", inv);
        var inactive = issue.StatusAtIssue != PrescriptionStatuses.Active;
        var p = envelope.Patient;
        var input = new
        {
            kind = issue.IssueKind == PrescriptionIssueKinds.Print ? "Prescription" : "Prescription record copy",
            templateVersion = issue.TemplateVersion,
            issueId = issue.Id.ToString("N")[..8],
            issued = Minute(issue.IssuedAtUtc),
            produced = Minute(producedAtUtc),
            prescribed = Day(r.PrescribedAtUtc),
            patient = new
            {
                p.Name, dateOfBirth = DateOnly.Parse(p.DateOfBirth, inv).ToString("yyyy-MM-dd", inv), p.Mrn,
                address = string.Join(", ", new[] { p.AddressLine1, p.AddressLine2, $"{p.City}, {p.State} {p.PostalCode}", p.Country == "US" ? null : p.Country }.Where(x => !string.IsNullOrWhiteSpace(x))),
            },
            prescriber = envelope.Prescriber,
            practice = envelope.Practice,
            medication = r.Medication,
            dose = $"{r.DoseValue.ToString("0.###", inv)} {r.DoseUnit}",
            frequency = FrequencyLabel(r.Frequency),
            directions = r.Instructions,
            quantity = Quantity(r.QuantityValue!.Value, r.QuantityUnit!),
            refills = r.Refills!.Value,
            status = issue.StatusAtIssue,
            watermark = inactive ? $"{issue.StatusAtIssue.ToUpperInvariant()} — DO NOT DISPENSE" : null,
            statusLine = inactive ? $"{issue.StatusAtIssue}{(endedAtUtc is { } at ? " on " + Day(at) : "")}{(string.IsNullOrWhiteSpace(endedReason) ? "" : ": " + endedReason)}" : null,
            boundary = Boundary,
        };
        return new Request("prescription", issue.TemplateVersion, JsonSerializer.Serialize(input, Json), $"Prescription-{issue.Id.ToString("N")[..8]}.pdf");
    }
}
