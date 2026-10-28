using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using MoneyType = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Imports;

/// <summary>What the dry-run of an opening-balance file decided: every row's outcome and the total of the rows that would be loaded (the figure the operator checks against the old system).</summary>
internal sealed record BalanceEvaluation(List<ImportRowOutcome> Outcomes, decimal ValidAmount);

/// <summary>
/// ALV-018-C02 slice 5a: judges every row of an opening-balance file WITHOUT writing anything, in bulk. A migrated balance is a first entry on a patient's ledger, never a change to an existing one, so the rules are strict:
/// 1. the row's own values (all problems at once, as fixed messages, never a value from the file): the patient's names and birth date; an amount owed that is more than zero, in dollars and cents, within the ceiling
///    (a credit is refused: only money the patient owes is loaded); an optional "as of" date that is a real date not in the future; an optional currency that is the practice's own (USD);
/// 2. the patient: exactly one registered, active, not merged-away patient with that name and birth date (<see cref="ImportPatientLookup"/>);
/// 3. one balance for each patient in a file: a later row for the same patient is Rejected (<c>patient_repeated_in_file</c>) and names the first row;
/// 4. the patient's ledger must hold no entry at all (<c>account_not_empty</c>): an opening balance is never added to a history, so a balance can never be loaded twice or overwrite what is there.
/// Existing ledgers are looked at once per group of patients, not once per row. The result for a row is Valid or Rejected. Failure modes handled: every value problem, an unknown or ambiguous patient, a repeat in the file, a
/// patient who already has ledger entries. Not handled: an entry made after the dry-run (the commit checks again, slice 5b). No external call is made.
/// </summary>
internal sealed class ImportBalanceEvaluator(AlveraDbContext db, IPracticeClock clock)
{
    private const int ChunkSize = 1_000;

    private sealed record Parsed(int RowNumber, string First, string Last, DateOnly Dob, decimal Amount, Dictionary<string, string> Errors);

    public async Task<BalanceEvaluation> EvaluateAsync(CsvTable table, IReadOnlyDictionary<string, string> mapping, CancellationToken ct)
    {
        var index = ImportPatientRows.FieldIndex(table, mapping);
        var parsed = new List<Parsed>(table.Rows.Count);
        for (var i = 0; i < table.Rows.Count; i++) parsed.Add(Parse(i + 2, table.Rows[i], index));

        var patients = await ImportPatientLookup.LoadAsync(db, parsed.Where(p => p.Dob != default).Select(p => p.Dob).Distinct().ToList(), ct);
        var resolved = new Dictionary<int, Guid>();
        foreach (var row in parsed)
        {
            if (row.Errors.Count > 0 || row.First.Length == 0) continue;
            if (ImportPatientLookup.Resolve(row.First, row.Last, row.Dob, patients, row.Errors) is { } patient) resolved[row.RowNumber] = patient.Id;
        }

        var withHistory = await PatientsWithEntriesAsync(resolved.Values.Distinct().ToList(), ct);
        var firstRowOf = new Dictionary<Guid, int>();                                    // the first row of the file for each patient
        var outcomes = new List<ImportRowOutcome>(parsed.Count);
        var total = 0m;
        foreach (var row in parsed)
        {
            if (row.Errors.Count > 0) { outcomes.Add(Reject(row.RowNumber, ImportRowReason.InvalidFields, row.Errors)); continue; }
            var patientId = resolved[row.RowNumber];
            if (firstRowOf.TryGetValue(patientId, out var earlier))
            {
                outcomes.Add(Reject(row.RowNumber, ImportRowReason.PatientRepeatedInFile, new() { ["patient"] = "An earlier row of this file already carries a balance for this patient." }, earlier));
                continue;
            }

            firstRowOf[patientId] = row.RowNumber;
            if (withHistory.Contains(patientId))
            {
                outcomes.Add(Reject(row.RowNumber, ImportRowReason.AccountNotEmpty, new() { ["patient"] = "This patient's account already has entries, so an opening balance cannot be added." }));
                continue;
            }

            outcomes.Add(new ImportRowOutcome(row.RowNumber, ImportRowStatus.Valid, null, null, [patientId], null));
            total += row.Amount;
        }

        return new BalanceEvaluation(outcomes, total);
    }

    /// <summary>One row read for the commit: the patient and the amount, or the fixed messages saying why the row no longer reads (the repeat and ledger checks are the commit's own, through the save key and the billing rules).</summary>
    public sealed record ResolvedBalance(int RowNumber, Guid? PatientId, decimal Amount, Dictionary<string, string> Errors);

    public async Task<List<ResolvedBalance>> ResolveRowsAsync(CsvTable table, IReadOnlyDictionary<string, string> mapping, IReadOnlySet<int> rowNumbers, CancellationToken ct)
    {
        var index = ImportPatientRows.FieldIndex(table, mapping);
        var parsed = rowNumbers.Order().Where(n => n >= 2 && n - 2 < table.Rows.Count).Select(n => Parse(n, table.Rows[n - 2], index)).ToList();
        var patients = await ImportPatientLookup.LoadAsync(db, parsed.Where(p => p.Dob != default).Select(p => p.Dob).Distinct().ToList(), ct);
        var result = new List<ResolvedBalance>(parsed.Count);
        foreach (var row in parsed)
        {
            var patient = row.Errors.Count > 0 ? null : ImportPatientLookup.Resolve(row.First, row.Last, row.Dob, patients, row.Errors);
            result.Add(new ResolvedBalance(row.RowNumber, patient?.Id, row.Amount, row.Errors));
        }

        return result;
    }

    private static ImportRowOutcome Reject(int row, string reason, Dictionary<string, string> errors, int? duplicateOfRow = null) =>
        new(row, ImportRowStatus.Rejected, reason, errors, null, duplicateOfRow);

    private async Task<HashSet<Guid>> PatientsWithEntriesAsync(IReadOnlyList<Guid> patientIds, CancellationToken ct)
    {
        var found = new HashSet<Guid>();
        foreach (var chunk in patientIds.Chunk(ChunkSize))
            found.UnionWith(await db.BillingEntries.AsNoTracking().Where(e => chunk.Contains(e.PatientId)).Select(e => e.PatientId).Distinct().ToListAsync(ct));
        return found;
    }

    private Parsed Parse(int rowNumber, string[] row, IReadOnlyDictionary<string, int> index)
    {
        var errors = new Dictionary<string, string>();
        string? Cell(string field) => index.TryGetValue(field, out var i) && i >= 0 ? row[i].Trim() : null;
        string Name(string field, string label)
        {
            var v = Cell(field);
            if (string.IsNullOrEmpty(v)) { errors[field] = $"{label} is required."; return ""; }
            if (v.Length > 80) { errors[field] = $"{label} must be 80 characters or fewer."; return ""; }
            return v;
        }

        var first = Name("patientFirstName", "Patient first name");
        var last = Name("patientLastName", "Patient last name");
        var dob = default(DateOnly);
        var dobText = Cell("patientDateOfBirth");
        if (string.IsNullOrEmpty(dobText)) errors["patientDateOfBirth"] = "Patient date of birth is required.";
        else if (!DateOnly.TryParseExact(dobText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dob)) errors["patientDateOfBirth"] = "Patient date of birth must be a real date in the form yyyy-MM-dd.";

        var amount = 0m;
        var amountText = Cell("amount");
        if (string.IsNullOrEmpty(amountText)) errors["amount"] = "Amount owed is required.";
        else if (!decimal.TryParse(amountText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) errors["amount"] = "Amount owed must be a number such as 125.50, with no currency sign or thousands separator.";
        else
        {
            var problems = new Dictionary<string, string>();
            if (amount < 0) errors["amount"] = "A credit cannot be imported. Only an amount the patient owes (more than zero) is loaded.";
            else if (BillingRules.CleanPositiveAmount(problems, amount, "opening balance") is null) errors["amount"] = problems["amount"];
        }

        var asOf = Cell("asOfDate");
        if (!string.IsNullOrEmpty(asOf))
        {
            if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) errors["asOfDate"] = "The as-of date must be a real date in the form yyyy-MM-dd.";
            else if (date > DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).DateTime)) errors["asOfDate"] = "The as-of date cannot be in the future.";
        }

        var currency = Cell("currency");
        if (!string.IsNullOrEmpty(currency) && !string.Equals(currency, MoneyType.Currency, StringComparison.OrdinalIgnoreCase)) errors["currency"] = $"Only {MoneyType.Currency} balances can be imported.";
        return new Parsed(rowNumber, first, last, dob, amount, errors);
    }
}
