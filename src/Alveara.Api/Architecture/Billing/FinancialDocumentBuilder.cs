using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>One entry as the document builder needs it. <c>ServiceDateUtc</c> is the date the charge ages from: the procedure's performed time (as amended at the document's own instant) or, for a charge with no completed procedure, when it was recorded.</summary>
public sealed record SnapshotEntry(int Number, string Kind, decimal Amount, int? ReversesNumber, int? RefundsNumber, int? ReplacesNumber, string? ProcedureCode, string? ProcedureDescription, string? Method,
    string? Reason, DateTimeOffset RecordedAtUtc, DateTimeOffset ServiceDateUtc);
public sealed record SnapshotAllocation(int Number, int PaymentNumber, int ChargeNumber, decimal Amount);
public sealed record SnapshotRelease(int Number, int PaymentNumber, int ChargeNumber, decimal Amount, int? RefundNumber);
public sealed record LedgerSnapshot(IReadOnlyList<SnapshotEntry> Entries, IReadOnlyList<SnapshotAllocation> Allocations, IReadOnlyList<SnapshotRelease> Releases);

/// <summary>
/// ALV-007-C02: builds the canonical content of a statement or receipt from a ledger snapshot and cut-offs, with no database. The same function builds a preview, the check at issue, a reprint and an export, so they
/// can never disagree. Only what is at or before the cut-offs is read (so appending to the ledger never changes a document), every amount is fixed two-place text, every time is fixed UTC text, and the
/// canonical bytes are the JSON of the content in declaration order, hashed with SHA-256 (lower-case hexadecimal). Aging counts whole UTC days from the service date to the document's own instant.
/// </summary>
public static class FinancialDocumentBuilder
{
    public const int TemplateVersion = 1;

    private static readonly JsonSerializerOptions Canonical = new() { WriteIndented = false, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };

    public static string Money(decimal amount) => AppMoney.FromDecimal(amount).Amount.ToString("F2", CultureInfo.InvariantCulture);
    public static string Instant(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    public static string Bucket(int ageDays) => ageDays <= 30 ? "Current" : ageDays <= 60 ? "31-60" : ageDays <= 90 ? "61-90" : "Over 90";

    public static string CanonicalJson(object content) => JsonSerializer.Serialize(content, content.GetType(), Canonical);
    public static string Hash(string canonicalJson) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    public static string HashOf(object content) => Hash(CanonicalJson(content));

    private sealed record Cut(IReadOnlyList<SnapshotEntry> Entries, IReadOnlyList<LedgerEntry> Math, IReadOnlyList<LedgerAllocation> Allocations, IReadOnlyList<LedgerRelease> Releases);

    private static Cut CutAt(LedgerSnapshot s, FinancialCutOffs c)
    {
        var entries = s.Entries.Where(e => e.Number <= c.EntryNumber).OrderBy(e => e.Number).ToList();
        return new Cut(entries,
            entries.Select(e => new LedgerEntry(e.Number, e.Kind, e.Amount, e.ReversesNumber, e.RefundsNumber)).ToList(),
            s.Allocations.Where(a => a.Number <= c.AllocationNumber).Select(a => new LedgerAllocation(a.PaymentNumber, a.ChargeNumber, a.Amount)).ToList(),
            s.Releases.Where(r => r.Number <= c.ReleaseNumber).Select(r => new LedgerRelease(r.PaymentNumber, r.ChargeNumber, r.Amount, r.RefundNumber)).ToList());
    }

    private static int AgeDays(DateTimeOffset asOf, DateTimeOffset service) => Math.Max(0, (asOf.UtcDateTime.Date - service.UtcDateTime.Date).Days);

    /// <summary>The open charges at the cut-offs with their age, and the totals per aging bucket.</summary>
    public static (IReadOnlyList<DocumentOpenCharge> Charges, DocumentAging Aging) AgingAt(LedgerSnapshot snapshot, FinancialCutOffs cutOffs)
    {
        var cut = CutAt(snapshot, cutOffs);
        var charges = new List<DocumentOpenCharge>();
        var buckets = new Dictionary<string, AppMoney> { ["Current"] = AppMoney.Zero, ["31-60"] = AppMoney.Zero, ["61-90"] = AppMoney.Zero, ["Over 90"] = AppMoney.Zero };
        foreach (var (number, open) in BillingLedgerMath.OpenCharges(cut.Math, cut.Allocations, cut.Releases))
        {
            var e = cut.Entries.First(x => x.Number == number);
            var age = AgeDays(cutOffs.AsOfUtc, e.ServiceDateUtc);
            var bucket = Bucket(age);
            buckets[bucket] += AppMoney.FromDecimal(open);
            charges.Add(new DocumentOpenCharge(number, e.ProcedureCode, e.ProcedureDescription, Money(e.Amount), Money(BillingLedgerMath.Paid(cut.Math, cut.Allocations, number, cut.Releases)), Money(open), Instant(e.ServiceDateUtc), age, bucket));
        }
        return (charges, new DocumentAging(Money(buckets["Current"].Amount), Money(buckets["31-60"].Amount), Money(buckets["61-90"].Amount), Money(buckets["Over 90"].Amount)));
    }

    public static StatementContent Statement(LedgerSnapshot snapshot, FinancialCutOffs cutOffs, DocumentEnvelope envelope)
    {
        var cut = CutAt(snapshot, cutOffs);
        var running = BillingLedgerMath.RunningBalances(cut.Math);
        var lines = cut.Entries.Select((e, i) => new DocumentLine(e.Number, e.Kind, Money(e.Amount), Money(running[i]), e.ProcedureCode, e.ProcedureDescription, e.Method, e.Reason, Instant(e.RecordedAtUtc),
            e.ReversesNumber, e.RefundsNumber, e.ReplacesNumber)).ToList();
        var (open, aging) = AgingAt(snapshot, cutOffs);
        var unapplied = cut.Entries.Where(e => e.Kind == BillingKinds.Payment)
            .Select(e => (e.Number, Left: BillingLedgerMath.Unapplied(cut.Math, cut.Allocations, e.Number, cut.Releases))).Where(p => p.Left > 0).Select(p => new DocumentUnapplied(p.Number, Money(p.Left))).ToList();
        var openTotal = open.Aggregate(AppMoney.Zero, (sum, o) => sum + AppMoney.FromDecimal(decimal.Parse(o.Open, CultureInfo.InvariantCulture))).Amount;
        var unappliedTotal = unapplied.Aggregate(AppMoney.Zero, (sum, u) => sum + AppMoney.FromDecimal(decimal.Parse(u.Amount, CultureInfo.InvariantCulture))).Amount;
        return new StatementContent(FinancialDocumentTypes.Statement, TemplateVersion, Instant(cutOffs.AsOfUtc), envelope.Currency, envelope.Patient, envelope.Addressee, lines, open, unapplied, aging,
            Money(BillingLedgerMath.Balance(cut.Math)), Money(openTotal), Money(unappliedTotal), envelope.Practice);
    }

    /// <summary>The receipt for one payment at the cut-offs. The payment must exist at them and must not have been reversed by then.</summary>
    public static ReceiptContent Receipt(LedgerSnapshot snapshot, FinancialCutOffs cutOffs, DocumentEnvelope envelope, int paymentNumber)
    {
        var cut = CutAt(snapshot, cutOffs);
        var payment = cut.Entries.FirstOrDefault(e => e.Number == paymentNumber && e.Kind == BillingKinds.Payment) ?? throw new ArgumentException($"Payment {paymentNumber} does not exist at the cut-offs.", nameof(paymentNumber));
        if (BillingLedgerMath.Reversed(cut.Math).Contains(paymentNumber)) throw new ArgumentException($"Payment {paymentNumber} had been reversed at the cut-offs.", nameof(paymentNumber));
        var applications = cut.Allocations.Where(a => a.PaymentNumber == paymentNumber).GroupBy(a => a.ChargeNumber).OrderBy(g => g.Key)
            .Select(g => (Charge: g.Key, Net: AppMoney.FromDecimal(g.Sum(a => a.Amount)) - cut.Releases.Where(r => r.PaymentNumber == paymentNumber && r.ChargeNumber == g.Key).Aggregate(AppMoney.Zero, (sum, r) => sum + AppMoney.FromDecimal(r.Amount))))
            .Where(x => x.Net.Amount > 0)
            .Select(x => { var c = cut.Entries.First(e => e.Number == x.Charge); return new ReceiptApplication(x.Charge, c.ProcedureCode, c.ProcedureDescription, Money(x.Net.Amount)); }).ToList();
        return new ReceiptContent(FinancialDocumentTypes.Receipt, TemplateVersion, Instant(cutOffs.AsOfUtc), envelope.Currency, envelope.Patient, envelope.Addressee, paymentNumber, Money(-payment.Amount), payment.Method,
            Instant(payment.RecordedAtUtc), applications, Money(BillingLedgerMath.Unapplied(cut.Math, cut.Allocations, paymentNumber, cut.Releases)), Money(BillingLedgerMath.Refunded(cut.Math, paymentNumber)),
            Money(BillingLedgerMath.Balance(cut.Math)), envelope.Practice);
    }

    /// <summary>The statement as CSV text: one row per ledger line, then the totals. Text that a spreadsheet could read as a formula is prefixed with an apostrophe.</summary>
    public static string StatementCsv(StatementContent s)
    {
        var sb = new StringBuilder();
        sb.Append("Number,Kind,Amount,BalanceAfter,ProcedureCode,Description,Method,Reason,RecordedAtUtc\r\n");
        foreach (var l in s.Lines)
            sb.Append(string.Join(',', l.Number.ToString(CultureInfo.InvariantCulture), Cell(l.Kind), l.Amount, l.BalanceAfter, Cell(l.ProcedureCode), Cell(l.ProcedureDescription), Cell(l.Method), Cell(l.Reason), l.RecordedAtUtc)).Append("\r\n");
        sb.Append(string.Join(',', "", Cell("Balance owed"), s.Balance, "", "", "", "", "", s.AsOfUtc)).Append("\r\n");
        return sb.ToString();
    }

    private static string Cell(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Length > 0 && "=+-@\t\r".Contains(text[0]) ? "'" + text : text;
        return t.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + t.Replace("\"", "\"\"") + "\"" : t;
    }
}
