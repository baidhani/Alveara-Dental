using System.Globalization;
using System.Text.Json;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-N026 S5: what a statement or receipt PDF is made from. The template receives the document's own issued content exactly as the hash check rebuilt it (never current data), plus display values
/// worked out here: practice-local dates for every instant in it, the issue and production times, and - for a document issued before PDFs (template versions 1-3) - the owner's legacy reprint label.
/// Version 4 renders with <c>statement/v4</c> or <c>receipt/v4</c>; versions 1-3 with <c>financial-legacy/v1</c>.
/// </summary>
public static class FinancialDocumentPdf
{
    public const string LegacyLabelFormat = "Reprint generated {0} from the verified record. This is not the original browser printout.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    public sealed record Request(string TemplateId, string TemplateVersion, string InputJson, string FileName);

    public static Request For(IssuedDocumentDetail detail, IPracticeClock clock, DateTimeOffset producedAtUtc)
    {
        var d = detail.Document;
        var isStatement = detail.Statement is not null;
        if (isStatement == (detail.Receipt is not null)) throw new InvalidOperationException("A financial document is either a statement or a receipt.");
        var legacy = d.TemplateVersion < 4;
        string Day(string isoUtc) => LocalDay(DateTimeOffset.Parse(isoUtc, CultureInfo.InvariantCulture), clock);

        var dates = new Dictionary<string, string>();
        if (detail.Statement is { } s)
        {
            foreach (var l in s.Lines) dates[$"line{l.Number}"] = Day(l.RecordedAtUtc);
            foreach (var c in s.OpenCharges) dates[$"service{c.Number}"] = Day(c.ServiceDateUtc);
            dates["asOf"] = Day(s.AsOfUtc);
        }
        if (detail.Receipt is { } r)
        {
            dates["received"] = Day(r.ReceivedAtUtc);
            dates["asOf"] = Day(r.AsOfUtc);
        }
        var produced = LocalMinute(producedAtUtc, clock);
        var input = new
        {
            kind = isStatement ? "Statement" : "Receipt",
            documentNumber = d.DocumentNumber,
            templateVersion = d.TemplateVersion,
            issued = LocalMinute(d.IssuedAtUtc, clock),
            produced,
            legacyLabel = legacy ? string.Format(CultureInfo.InvariantCulture, LegacyLabelFormat, produced) : null,
            replacesNumber = d.ReplacesNumber,
            dates,
            statement = detail.Statement,
            receipt = detail.Receipt,
        };
        var (id, version) = legacy ? ("financial-legacy", "v1") : (isStatement ? "statement" : "receipt", $"v{d.TemplateVersion}");
        return new Request(id, version, JsonSerializer.Serialize(input, Json), $"{(isStatement ? "Statement" : "Receipt")}-{d.DocumentNumber}.pdf");
    }

    private static string LocalDay(DateTimeOffset utc, IPracticeClock clock) => clock.ToPracticeLocal(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string LocalMinute(DateTimeOffset utc, IPracticeClock clock) => clock.ToPracticeLocal(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
