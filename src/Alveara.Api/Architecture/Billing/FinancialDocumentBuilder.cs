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
/// canonical bytes are the JSON of the content in declaration order, hashed with SHA-256 (lower-case hexadecimal).
///
/// ALV-N021 A1 (A1-DECISION-01): every document carries the template version it was built with, and is always rebuilt with that version.
/// - <b>Version 1</b> (documents issued before ALV-N021): aging counts whole UTC days from the service date to the document's own instant. Kept exactly, so a version-1 statement or receipt rebuilds byte
///   for byte as issued (proved against bytes captured from the original builder).
/// - <b>Version 2</b> (current): aging counts practice-local calendar days, from a frozen <see cref="DocumentAgingBasis"/> made when the document is previewed (each instant converted with its own zone
///   offset, so DST is handled) and carried in the envelope and the content. Thresholds, amounts and cut-offs are unchanged.
/// - <b>Version 3</b> (ALV-N024, current): as version 2, plus each reversed line names its reversal (<c>ReversedByNumber</c>, F-117) and the statement carries <c>AdjustmentTotal</c>, the adjustments
///   that are in the balance but in no charge-age bucket (F-116). Receipts differ only in the version they carry.
/// - Any other version is refused (<see cref="UnsupportedTemplateVersionException"/>); nothing ever falls back to the current version.
/// </summary>
public static class FinancialDocumentBuilder
{
    /// <summary>The version new previews and issues use.</summary>
    public const int CurrentTemplateVersion = 3;
    public const string PracticeLocalDaysPolicy = "PracticeLocalCalendarDays";

    public static bool IsSupported(int templateVersion) => templateVersion is 1 or 2 or 3;

    public static void RequireSupported(int templateVersion)
    {
        if (!IsSupported(templateVersion)) throw new UnsupportedTemplateVersionException(templateVersion);
    }

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

    /// <summary>Version 1: whole UTC days (the documented rule of ALV-007-C02, kept for documents issued under it).</summary>
    private static int UtcAgeDays(DateTimeOffset asOf, DateTimeOffset service) => Math.Max(0, (asOf.UtcDateTime.Date - service.UtcDateTime.Date).Days);

    private static string LocalDate(DateTimeOffset instant, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Version 2: the frozen practice-day basis for the charges open at the cut-offs. Each instant is converted with the offset that applied at that instant, never with one fixed current offset and never in
    /// the server's or browser's own zone.
    /// </summary>
    public static DocumentAgingBasis PracticeBasis(LedgerSnapshot snapshot, FinancialCutOffs cutOffs, TimeZoneInfo zone)
    {
        var cut = CutAt(snapshot, cutOffs);
        var days = BillingLedgerMath.OpenCharges(cut.Math, cut.Allocations, cut.Releases)
            .Select(o => new DocumentServiceDay(o.Charge, LocalDate(cut.Entries.First(e => e.Number == o.Charge).ServiceDateUtc, zone))).OrderBy(d => d.Number).ToList();
        return new DocumentAgingBasis(PracticeLocalDaysPolicy, zone.Id, Instant(cutOffs.AsOfUtc), LocalDate(cutOffs.AsOfUtc, zone), days);
    }

    private static DateOnly ParseDay(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : throw new ArgumentException($"The aging basis holds an unreadable date ({text}).");

    /// <summary>The basis must be for this document: the same policy and instant, and a date for every charge open at the cut-offs.</summary>
    private static Func<int, int> PracticeAges(DocumentAgingBasis basis, FinancialCutOffs cutOffs)
    {
        if (basis.Policy != PracticeLocalDaysPolicy) throw new ArgumentException($"The aging basis has an unknown policy ({basis.Policy}).");
        if (basis.AsOfUtc != Instant(cutOffs.AsOfUtc)) throw new ArgumentException("The aging basis was made for a different instant than the document's cut-offs.");
        var asOf = ParseDay(basis.AsOfLocalDate);
        var service = basis.ServiceDays.ToDictionary(d => d.Number, d => ParseDay(d.ServiceLocalDate));
        return number => service.TryGetValue(number, out var day) ? Math.Max(0, asOf.DayNumber - day.DayNumber) : throw new ArgumentException($"The aging basis has no service date for charge {number}.");
    }

    /// <summary>
    /// The open charges at the cut-offs with their age, and the totals per aging bucket. <paramref name="basis"/> null ages by the version-1 rule (UTC days); otherwise by the version-2 rule from that basis,
    /// which must match the cut-offs (an <see cref="ArgumentException"/> otherwise).
    /// </summary>
    public static (IReadOnlyList<DocumentOpenCharge> Charges, DocumentAging Aging) AgingAt(LedgerSnapshot snapshot, FinancialCutOffs cutOffs, DocumentAgingBasis? basis)
    {
        var cut = CutAt(snapshot, cutOffs);
        var practiceAge = basis is null ? null : PracticeAges(basis, cutOffs);
        var charges = new List<DocumentOpenCharge>();
        var buckets = new Dictionary<string, AppMoney> { ["Current"] = AppMoney.Zero, ["31-60"] = AppMoney.Zero, ["61-90"] = AppMoney.Zero, ["Over 90"] = AppMoney.Zero };
        foreach (var (number, open) in BillingLedgerMath.OpenCharges(cut.Math, cut.Allocations, cut.Releases))
        {
            var e = cut.Entries.First(x => x.Number == number);
            var age = practiceAge is null ? UtcAgeDays(cutOffs.AsOfUtc, e.ServiceDateUtc) : practiceAge(number);
            var bucket = Bucket(age);
            buckets[bucket] += AppMoney.FromDecimal(open);
            charges.Add(new DocumentOpenCharge(number, e.ProcedureCode, e.ProcedureDescription, Money(e.Amount), Money(BillingLedgerMath.Paid(cut.Math, cut.Allocations, number, cut.Releases)), Money(open), Instant(e.ServiceDateUtc), age, bucket));
        }
        return (charges, new DocumentAging(Money(buckets["Current"].Amount), Money(buckets["31-60"].Amount), Money(buckets["61-90"].Amount), Money(buckets["Over 90"].Amount)));
    }

    /// <summary>The statement as the given template version builds it. Version 2 needs the envelope's frozen aging basis; version 1 ignores any basis.</summary>
    public static StatementContent Statement(LedgerSnapshot snapshot, FinancialCutOffs cutOffs, DocumentEnvelope envelope, int templateVersion)
    {
        RequireSupported(templateVersion);
        var basis = templateVersion == 1 ? null : envelope.AgingBasis ?? throw new ArgumentException($"A version-{templateVersion} statement needs its aging basis.");
        var cut = CutAt(snapshot, cutOffs);
        var running = BillingLedgerMath.RunningBalances(cut.Math);
        var reversedBy = templateVersion >= 3 ? cut.Entries.Where(e => e.ReversesNumber is not null).ToDictionary(e => e.ReversesNumber!.Value, e => e.Number) : [];
        var lines = cut.Entries.Select((e, i) => new DocumentLine(e.Number, e.Kind, Money(e.Amount), Money(running[i]), e.ProcedureCode, e.ProcedureDescription, e.Method, e.Reason, Instant(e.RecordedAtUtc),
            e.ReversesNumber, e.RefundsNumber, e.ReplacesNumber, reversedBy.TryGetValue(e.Number, out var by) ? by : null)).ToList();
        var (open, aging) = AgingAt(snapshot, cutOffs, basis);
        var unapplied = cut.Entries.Where(e => e.Kind == BillingKinds.Payment)
            .Select(e => (e.Number, Left: BillingLedgerMath.Unapplied(cut.Math, cut.Allocations, e.Number, cut.Releases))).Where(p => p.Left > 0).Select(p => new DocumentUnapplied(p.Number, Money(p.Left))).ToList();
        var openTotal = open.Aggregate(AppMoney.Zero, (sum, o) => sum + AppMoney.FromDecimal(decimal.Parse(o.Open, CultureInfo.InvariantCulture))).Amount;
        var unappliedTotal = unapplied.Aggregate(AppMoney.Zero, (sum, u) => sum + AppMoney.FromDecimal(decimal.Parse(u.Amount, CultureInfo.InvariantCulture))).Amount;
        return new StatementContent(FinancialDocumentTypes.Statement, templateVersion, Instant(cutOffs.AsOfUtc), envelope.Currency, envelope.Patient, envelope.Addressee, lines, open, unapplied, aging,
            Money(BillingLedgerMath.Balance(cut.Math)), Money(openTotal), Money(unappliedTotal), envelope.Practice, basis,
            templateVersion >= 3 ? Money(BillingLedgerMath.LiveAdjustments(cut.Math)) : null);
    }

    /// <summary>The receipt for one payment at the cut-offs. The payment must exist at them and must not have been reversed by then. A receipt has no aging; versions 1 and 2 differ only in the version they carry.</summary>
    public static ReceiptContent Receipt(LedgerSnapshot snapshot, FinancialCutOffs cutOffs, DocumentEnvelope envelope, int paymentNumber, int templateVersion)
    {
        RequireSupported(templateVersion);
        var cut = CutAt(snapshot, cutOffs);
        var payment = cut.Entries.FirstOrDefault(e => e.Number == paymentNumber && e.Kind == BillingKinds.Payment) ?? throw new ArgumentException($"Payment {paymentNumber} does not exist at the cut-offs.", nameof(paymentNumber));
        if (BillingLedgerMath.Reversed(cut.Math).Contains(paymentNumber)) throw new ArgumentException($"Payment {paymentNumber} had been reversed at the cut-offs.", nameof(paymentNumber));
        var applications = cut.Allocations.Where(a => a.PaymentNumber == paymentNumber).GroupBy(a => a.ChargeNumber).OrderBy(g => g.Key)
            .Select(g => (Charge: g.Key, Net: AppMoney.FromDecimal(g.Sum(a => a.Amount)) - cut.Releases.Where(r => r.PaymentNumber == paymentNumber && r.ChargeNumber == g.Key).Aggregate(AppMoney.Zero, (sum, r) => sum + AppMoney.FromDecimal(r.Amount))))
            .Where(x => x.Net.Amount > 0)
            .Select(x => { var c = cut.Entries.First(e => e.Number == x.Charge); return new ReceiptApplication(x.Charge, c.ProcedureCode, c.ProcedureDescription, Money(x.Net.Amount)); }).ToList();
        return new ReceiptContent(FinancialDocumentTypes.Receipt, templateVersion, Instant(cutOffs.AsOfUtc), envelope.Currency, envelope.Patient, envelope.Addressee, paymentNumber, Money(-payment.Amount), payment.Method,
            Instant(payment.RecordedAtUtc), applications, Money(BillingLedgerMath.Unapplied(cut.Math, cut.Allocations, paymentNumber, cut.Releases)), Money(BillingLedgerMath.Refunded(cut.Math, paymentNumber)),
            Money(BillingLedgerMath.Balance(cut.Math)), envelope.Practice);
    }

    /// <summary>
    /// ALV-N024 (CR-2, route 1): the statement as CSV, a LIVE EXPORT of the issued content - never stored and never part of its hash, so every version gets the current structure: an identity block
    /// (practice, patient, statement number, as-of time and balance), a blank line, one row per ledger line with practice-local times and the zone named, a ReversedBy column (filled only where the content
    /// carries it: version 3; empty for versions 1 and 2, never recomputed), then the balance row. Text a spreadsheet could read as a formula is prefixed with an apostrophe.
    /// </summary>
    public static string StatementCsv(StatementContent s, int statementNumber, TimeZoneInfo zone)
    {
        string Local(string utcText) => DateTimeOffset.TryParse(utcText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : utcText;
        var sb = new StringBuilder();
        void Row(params string[] cells) => sb.Append(string.Join(',', cells)).Append("\r\n");
        Row(Cell("Practice"), Cell(s.Practice?.Name));
        Row(Cell("Patient"), Cell(s.Patient.Name));
        Row(Cell("Statement"), statementNumber.ToString(CultureInfo.InvariantCulture));
        Row(Cell("As of"), Cell($"{Local(s.AsOfUtc)} ({zone.Id})"));
        Row(Cell("Balance owed"), s.Balance);
        Row();
        Row("Number", "Kind", "Amount", "BalanceAfter", "ProcedureCode", "Description", "Method", "Reason", Cell($"RecordedAt ({zone.Id})"), "ReversedBy");
        foreach (var l in s.Lines)
            Row(l.Number.ToString(CultureInfo.InvariantCulture), Cell(l.Kind), l.Amount, l.BalanceAfter, Cell(l.ProcedureCode), Cell(l.ProcedureDescription), Cell(l.Method), Cell(l.Reason), Local(l.RecordedAtUtc),
                l.ReversedByNumber?.ToString(CultureInfo.InvariantCulture) ?? "");
        Row("", Cell("Balance owed"), s.Balance, "", "", "", "", "", Local(s.AsOfUtc), "");
        return sb.ToString();
    }

    private static string Cell(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Length > 0 && "=+-@\t\r".Contains(text[0]) ? "'" + text : text;
        return t.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + t.Replace("\"", "\"\"") + "\"" : t;
    }
}

/// <summary>A document carries a template version this code does not know. It is refused, never rebuilt with the current version.</summary>
public sealed class UnsupportedTemplateVersionException(int version) : Exception($"Template version {version} is not supported.")
{
    public int Version { get; } = version;
}
