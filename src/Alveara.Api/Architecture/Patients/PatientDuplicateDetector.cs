using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients;

/// <summary>
/// ALV-003-C01: finds patients who might be the person being registered, so the front desk sees them BEFORE a second
/// record is created. It only ever reports: nothing here (or anywhere) merges records.
///
/// Two strengths:
/// - EXACT: same normalized name and birth date. This is STORY-003's duplicate rule and the database's unique key, so
///   it is refused outright, not overridable (it is the same person by every field the system holds for identity).
/// - LIKELY: a human should look, but the front desk may proceed. The same full name with a different birth date ALONE is still not
///   a match (a parent and child routinely share a name); ALV-N025 (F-145, F-210) adds it only with a matching contact detail (below).
///
/// Likely rules (all deterministic, and ALL require the same birth date - a shared phone, email or surname alone is a normal
/// family and must not raise a warning; each match names its reason for the comparison panel):
///   same birth date and same last name (twins, a typo in the first name);
///   same birth date and first name with a last name one or two typos away;
///   same birth date and same phone digits;
///   same birth date and same email;
///   ALV-N025: DIFFERENT birth date, same first and last name, and the same phone digits, email, or address line 1 with postal code
///   (<see cref="Merge.PatientMatchRules.DifferentBirthDateReasons"/>, shared with the duplicate scan and the import).
/// The candidate set is bounded (a database query on birth date, capped) so it stays cheap.
/// </summary>
public class PatientDuplicateDetector(AlveraDbContext db)
{
    private const int CandidateQueryCap = 500;
    private const int MaxReturned = 10;

    public async Task<IReadOnlyList<DuplicateCandidate>> FindAsync(CleanPatient input, Guid? excludePatientId, CancellationToken ct)
    {
        var probe = DuplicateProbe.From(input);

        var pool = await db.Patients.AsNoTracking()
            .Where(p => p.DateOfBirth == input.DateOfBirth && (excludePatientId == null || p.Id != excludePatientId))
            .Take(CandidateQueryCap)
            .ToListAsync(ct);

        // ALV-N025: the same name with another birth date - a second bounded query, by the stored last name (the comparison then normalizes both names)
        var namesakes = await db.Patients.AsNoTracking()
            .Where(p => p.LastName == input.LastName && p.DateOfBirth != input.DateOfBirth && (excludePatientId == null || p.Id != excludePatientId))
            .Take(CandidateQueryCap)
            .ToListAsync(ct);

        var found = new List<DuplicateCandidate>();
        foreach (var p in pool.Concat(namesakes))
        {
            var (exact, reasons) = p.DateOfBirth == input.DateOfBirth
                ? Compare(probe, p.FirstName, p.LastName, p.Phone, p.Email)
                : (false, CompareDifferentBirthDate(probe, p.FirstName, p.LastName, p.Phone, p.Email, p.AddressLine1, p.PostalCode));
            if (reasons.Count == 0) continue;

            found.Add(new DuplicateCandidate(p.Id, p.FirstName, p.MiddleName, p.LastName, p.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.Phone, p.Email, p.City, p.State, p.IsActive, reasons, exact));
        }

        return found.OrderByDescending(c => c.Exact).ThenByDescending(c => c.Reasons.Count).ThenBy(c => c.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.FirstName, StringComparer.OrdinalIgnoreCase).Take(MaxReturned).ToList();
    }

    /// <summary>
    /// The deterministic comparison of one person being registered against one registered person who has the same birth date (the caller selects by birth date). It is a pure function of the values, so the
    /// front desk's check (<see cref="FindAsync"/>) and the bulk import's dry-run (ALV-018-C02, which compares thousands of rows without a query each) apply exactly the same rules. No reason means no match.
    /// </summary>
    internal static (bool Exact, List<string> Reasons) Compare(DuplicateProbe probe, string existingFirst, string existingLast, string existingPhone, string? existingEmail)
    {
        var reasons = new List<string>();
        var pLast = Patient.NormalizeName(existingLast);
        var pFirst = Patient.NormalizeName(existingFirst);
        var samePhone = probe.PhoneDigits.Length >= 7 && PatientInput.DigitsOf(existingPhone) == probe.PhoneDigits;
        var exact = pLast == probe.Last && pFirst == probe.First;

        if (exact) reasons.Add("Same name and date of birth");
        else
        {
            if (pLast == probe.Last) reasons.Add("Same last name and date of birth");
            if (pFirst == probe.First && EditDistance(pLast, probe.Last) is > 0 and <= 2) reasons.Add("Same first name and date of birth, similar last name");
            if (samePhone) reasons.Add("Same phone number and date of birth");
            if (probe.Email is not null && string.Equals(existingEmail, probe.Email, StringComparison.OrdinalIgnoreCase)) reasons.Add("Same email and date of birth");
        }

        return (exact, reasons);
    }

    /// <summary>ALV-N025: one registered person with a DIFFERENT birth date - the shared rule; never exact, so the front desk may proceed after reviewing.</summary>
    internal static List<string> CompareDifferentBirthDate(DuplicateProbe probe, string existingFirst, string existingLast, string existingPhone, string? existingEmail, string? existingLine1, string? existingPostal) =>
        Merge.PatientMatchRules.DifferentBirthDateReasons(probe.Facts, new Merge.PatientFacts(existingFirst, existingLast, existingPhone, existingEmail, existingLine1, existingPostal)).ToList();

    /// <summary>Levenshtein distance between two already-normalized names.</summary>
    internal static int EditDistance(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[b.Length];
    }
}

/// <summary>The person being compared, with the values the comparison needs already normalized.</summary>
/// ALV-N025: <c>Facts</c> carries the raw values (with the address) for the different-birth-date rule; it is optional so existing callers are unchanged.
internal sealed record DuplicateProbe(string Last, string First, string PhoneDigits, string? Email, Merge.PatientFacts Facts = default)
{
    public static DuplicateProbe From(CleanPatient input) => new(Patient.NormalizeName(input.LastName), Patient.NormalizeName(input.FirstName), PatientInput.DigitsOf(input.Phone), input.Email,
        new Merge.PatientFacts(input.FirstName, input.LastName, input.Phone, input.Email, input.AddressLine1, input.PostalCode));
}
