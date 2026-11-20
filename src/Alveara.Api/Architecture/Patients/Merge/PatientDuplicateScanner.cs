using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>One side of a suggested merge: just what a person needs to recognise the record.</summary>
public sealed record PatientBrief(Guid Id, string FirstName, string? MiddleName, string LastName, string DateOfBirth, string Phone, string? Email, string City, string State, bool IsActive, DateTimeOffset CreatedAtUtc, Guid? HouseholdId);

/// <summary>
/// A pair of existing patients that may be the same person. <see cref="Score"/> (0 to 100) orders the list; <see cref="Reasons"/> says why in words; <see cref="SuggestedSurvivorId"/> is only a suggestion (the
/// administrator chooses which record survives); <see cref="SameHousehold"/> is shown because two people of one household with the same birth date and surname can be twins, not duplicates.
/// </summary>
public sealed record DuplicatePair(PatientBrief First, PatientBrief Second, int Score, IReadOnlyList<string> Reasons, Guid SuggestedSurvivorId, string SuggestionReason, bool SameHousehold);

/// <summary><see cref="Dismissed"/> is how many suggested pairs were left out because an administrator dismissed them as not a duplicate.</summary>
public sealed record DuplicateScanResult(IReadOnlyList<DuplicatePair> Pairs, bool Truncated, int PatientsConsidered, int Dismissed = 0);

/// <summary>What the matching rules look at, so the same rules serve the registration check and the scan.</summary>
/// ALV-N025: <c>AddressLine1</c> and <c>PostalCode</c> feed the different-birth-date rule only (optional, so the same-birth-date callers are unchanged).
public readonly record struct PatientFacts(string FirstName, string LastName, string Phone, string? Email, string? AddressLine1 = null, string? PostalCode = null);

/// <summary>
/// STORY-017: the matching rules, the same as the registration check's (<c>PatientDuplicateDetector</c>): every rule needs the same date of birth, because a shared surname, phone or email alone is a normal family. A test
/// holds the two in step. Weights are added and capped at 100. ALV-N025 adds the one lower-confidence exception, <see cref="DifferentBirthDateReasons"/>, which the registration
/// check, the import and the scan all call here.
/// </summary>
public static class PatientMatchRules
{
    public const string SameNameReason = "Same name and date of birth";
    public const string SameLastNameReason = "Same last name and date of birth";
    public const string SimilarLastNameReason = "Same first name and date of birth, similar last name";
    public const string SamePhoneReason = "Same phone number and date of birth";
    public const string SameEmailReason = "Same email and date of birth";
    // ALV-N025 (F-145, F-210): the same person typed with a different birth date (a mistyped year) - the same name and the same contact details
    public const string DifferentBirthPhoneReason = "Same name and phone number, different birth date";
    public const string DifferentBirthEmailReason = "Same name and email, different birth date";
    public const string DifferentBirthAddressReason = "Same name and address, different birth date";

    private static readonly Dictionary<string, int> Weights = new()
    {
        [SameNameReason] = 100, [SameLastNameReason] = 40, [SimilarLastNameReason] = 35, [SamePhoneReason] = 30, [SameEmailReason] = 30,
        [DifferentBirthPhoneReason] = 45, [DifferentBirthEmailReason] = 45, [DifferentBirthAddressReason] = 40,
    };

    /// <summary>The reasons two patients of the same birth date may be the same person (empty when none applies).</summary>
    public static IReadOnlyList<string> Reasons(PatientFacts a, PatientFacts b)
    {
        var aLast = Patient.NormalizeName(a.LastName); var bLast = Patient.NormalizeName(b.LastName);
        var aFirst = Patient.NormalizeName(a.FirstName); var bFirst = Patient.NormalizeName(b.FirstName);
        var aPhone = PatientInput.DigitsOf(a.Phone);
        var reasons = new List<string>();
        if (aLast == bLast && aFirst == bFirst) { reasons.Add(SameNameReason); return reasons; }
        if (aLast == bLast) reasons.Add(SameLastNameReason);
        if (aFirst == bFirst && PatientDuplicateDetector.EditDistance(aLast, bLast) is > 0 and <= 2) reasons.Add(SimilarLastNameReason);
        if (aPhone.Length >= 7 && PatientInput.DigitsOf(b.Phone) == aPhone) reasons.Add(SamePhoneReason);
        if (!string.IsNullOrEmpty(a.Email) && string.Equals(a.Email, b.Email, StringComparison.OrdinalIgnoreCase)) reasons.Add(SameEmailReason);
        return reasons;
    }

    /// <summary>
    /// ALV-N025 (F-145, F-210; lower confidence): two patients with DIFFERENT birth dates may still be one person when the normalised first and last names are the same
    /// AND the phone number, the email or the address (line 1 with the postal code) is the same. Empty when the names differ or no contact detail matches. The caller
    /// only asks this for a pair whose birth dates differ.
    /// </summary>
    public static IReadOnlyList<string> DifferentBirthDateReasons(PatientFacts a, PatientFacts b)
    {
        var reasons = new List<string>();
        if (Patient.NormalizeName(a.LastName) != Patient.NormalizeName(b.LastName) || Patient.NormalizeName(a.FirstName) != Patient.NormalizeName(b.FirstName)) return reasons;
        var aPhone = PatientInput.DigitsOf(a.Phone);
        if (aPhone.Length >= 7 && PatientInput.DigitsOf(b.Phone) == aPhone) reasons.Add(DifferentBirthPhoneReason);
        if (!string.IsNullOrEmpty(a.Email) && string.Equals(a.Email, b.Email, StringComparison.OrdinalIgnoreCase)) reasons.Add(DifferentBirthEmailReason);
        if (AddressKey(a) is { } key && key == AddressKey(b)) reasons.Add(DifferentBirthAddressReason);
        return reasons;
    }

    private static string? AddressKey(PatientFacts f) =>
        string.IsNullOrWhiteSpace(f.AddressLine1) || string.IsNullOrWhiteSpace(f.PostalCode) ? null : $"{Patient.NormalizeName(f.AddressLine1)}|{Patient.NormalizeName(f.PostalCode)}";

    public static int Score(IReadOnlyList<string> reasons) => Math.Min(100, reasons.Sum(r => Weights[r]));
}

/// <summary>
/// STORY-017: looks through the patients that already exist for pairs that may be the same person, and suggests them for a controlled merge. It only reads and reports: nothing here merges, changes or hides a record.
/// A record that was merged into another is never suggested again. Only patients who share a date of birth are compared (as in the registration check), the database picks the birth dates that more than one patient has, each pair is reported once, and the list is capped
/// (<see cref="MaxPairs"/>, strongest first; <see cref="DuplicateScanResult.Truncated"/> says when there were more) so a large practice cannot make the scan expensive. The suggested survivor is the active record,
/// otherwise the older one (the older chart usually holds more history), with the reason given; the administrator decides.
/// </summary>
public class PatientDuplicateScanner(AlveraDbContext db)
{
    public const int MaxPairs = 200;
    private const int MaxPerBirthDate = 100;            // a birth date shared by more than this many patients is compared on its first 100 (by registration time), which no real practice reaches

    public async Task<DuplicateScanResult> ScanAsync(CancellationToken ct, int maxPairs = MaxPairs)
    {
        maxPairs = Math.Clamp(maxPairs, 1, MaxPairs);
        var shared = await db.Patients.AsNoTracking().Where(p => p.MergedAtUtc == null).GroupBy(p => p.DateOfBirth).Where(g => g.Count() > 1).Select(g => g.Key).ToListAsync(ct);
        var rows = shared.Count == 0 ? [] : await db.Patients.AsNoTracking().Where(p => p.MergedAtUtc == null && shared.Contains(p.DateOfBirth))
            .OrderBy(p => p.DateOfBirth).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.FirstName, p.MiddleName, p.LastName, p.DateOfBirth, p.Phone, p.Email, p.City, p.State, p.IsActive, p.CreatedAtUtc, p.HouseholdId }).ToListAsync(ct);

        var pairs = new List<DuplicatePair>();
        foreach (var group in rows.GroupBy(r => r.DateOfBirth))
        {
            var members = group.Take(MaxPerBirthDate).Select(r => new PatientBrief(r.Id, r.FirstName, r.MiddleName, r.LastName, r.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.Phone, r.Email, r.City, r.State, r.IsActive, r.CreatedAtUtc, r.HouseholdId)).ToList();
            for (var i = 0; i < members.Count; i++)
                for (var j = i + 1; j < members.Count; j++)
                {
                    var a = members[i]; var b = members[j];
                    var reasons = PatientMatchRules.Reasons(new PatientFacts(a.FirstName, a.LastName, a.Phone, a.Email), new PatientFacts(b.FirstName, b.LastName, b.Phone, b.Email));
                    if (reasons.Count == 0) continue;
                    var (survivor, why) = Suggest(a, b);
                    pairs.Add(new DuplicatePair(a, b, PatientMatchRules.Score(reasons), reasons, survivor, why, a.HouseholdId is not null && a.HouseholdId == b.HouseholdId));
                }
        }
        pairs.AddRange(await DifferentBirthDatePairsAsync(ct));
        var dismissedPairs = await PatientDuplicateReviewService.ActivePairsAsync(db, ct);
        var dismissed = pairs.Count(p => dismissedPairs.Contains(PatientDuplicateReviewService.Pair(p.First.Id, p.Second.Id)));
        pairs = pairs.Where(p => !dismissedPairs.Contains(PatientDuplicateReviewService.Pair(p.First.Id, p.Second.Id))).ToList();
        var ordered = pairs.OrderByDescending(p => p.Score).ThenBy(p => p.First.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.First.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.First.Id).ThenBy(p => p.Second.Id).ToList();
        return new DuplicateScanResult(ordered.Take(maxPairs).ToList(), ordered.Count > maxPairs, rows.Count, dismissed);
    }

    /// <summary>
    /// ALV-N025 (F-145, F-210): the second pass - patients with the same first and last name but DIFFERENT birth dates, reported only when a contact detail also matches
    /// (<see cref="PatientMatchRules.DifferentBirthDateReasons"/>). The database picks the names that more than one patient has; each such name is compared on its first
    /// <see cref="MaxPerBirthDate"/> records, and a pair with the same birth date is left to the first pass.
    /// </summary>
    private async Task<List<DuplicatePair>> DifferentBirthDatePairsAsync(CancellationToken ct)
    {
        var names = await db.Patients.AsNoTracking().Where(p => p.MergedAtUtc == null).GroupBy(p => new { p.LastName, p.FirstName })
            .Where(g => g.Select(x => x.DateOfBirth).Distinct().Count() > 1).Select(g => g.Key.LastName).Distinct().ToListAsync(ct);
        var pairs = new List<DuplicatePair>();
        if (names.Count == 0) return pairs;
        var rows = await db.Patients.AsNoTracking().Where(p => p.MergedAtUtc == null && names.Contains(p.LastName))
            .OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.FirstName, p.MiddleName, p.LastName, p.DateOfBirth, p.Phone, p.Email, p.AddressLine1, p.PostalCode, p.City, p.State, p.IsActive, p.CreatedAtUtc, p.HouseholdId }).ToListAsync(ct);
        foreach (var group in rows.GroupBy(r => $"{Patient.NormalizeName(r.LastName)}|{Patient.NormalizeName(r.FirstName)}"))
        {
            var members = group.Take(MaxPerBirthDate).ToList();
            for (var i = 0; i < members.Count; i++)
                for (var j = i + 1; j < members.Count; j++)
                {
                    var x = members[i]; var y = members[j];
                    if (x.DateOfBirth == y.DateOfBirth) continue;
                    var reasons = PatientMatchRules.DifferentBirthDateReasons(new PatientFacts(x.FirstName, x.LastName, x.Phone, x.Email, x.AddressLine1, x.PostalCode),
                        new PatientFacts(y.FirstName, y.LastName, y.Phone, y.Email, y.AddressLine1, y.PostalCode));
                    if (reasons.Count == 0) continue;
                    var a = new PatientBrief(x.Id, x.FirstName, x.MiddleName, x.LastName, x.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Phone, x.Email, x.City, x.State, x.IsActive, x.CreatedAtUtc, x.HouseholdId);
                    var b = new PatientBrief(y.Id, y.FirstName, y.MiddleName, y.LastName, y.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), y.Phone, y.Email, y.City, y.State, y.IsActive, y.CreatedAtUtc, y.HouseholdId);
                    var (survivor, why) = Suggest(a, b);
                    pairs.Add(new DuplicatePair(a, b, PatientMatchRules.Score(reasons), reasons, survivor, why, a.HouseholdId is not null && a.HouseholdId == b.HouseholdId));
                }
        }
        return pairs;
    }

    /// <summary>The record to keep: the active one; when both (or neither) are active, the one registered first, then the lower id so the answer never flips between scans.</summary>
    public static (Guid SurvivorId, string Reason) Suggest(PatientBrief a, PatientBrief b)
    {
        if (a.IsActive != b.IsActive) return a.IsActive ? (a.Id, "The other record is inactive") : (b.Id, "The other record is inactive");
        if (a.CreatedAtUtc != b.CreatedAtUtc) return a.CreatedAtUtc < b.CreatedAtUtc ? (a.Id, "The older record, which usually holds more history") : (b.Id, "The older record, which usually holds more history");
        return a.Id.CompareTo(b.Id) < 0 ? (a.Id, "Registered at the same moment; the first by id") : (b.Id, "Registered at the same moment; the first by id");
    }
}
