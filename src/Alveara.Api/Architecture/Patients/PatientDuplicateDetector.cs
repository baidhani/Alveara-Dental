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
/// - LIKELY: a human should look, but the front desk may proceed. Deliberately NOT included: the same full name with a
///   different birth date (a parent and child routinely share a name), so STORY-003's behavior for that case is unchanged.
///
/// Likely rules (all deterministic, and ALL require the same birth date - a shared phone, email or surname alone is a normal
/// family and must not raise a warning; each match names its reason for the comparison panel):
///   same birth date and same last name (twins, a typo in the first name);
///   same birth date and first name with a last name one or two typos away;
///   same birth date and same phone digits;
///   same birth date and same email.
/// The candidate set is bounded (a database query on birth date, capped) so it stays cheap.
/// </summary>
public class PatientDuplicateDetector(AlveraDbContext db)
{
    private const int CandidateQueryCap = 500;
    private const int MaxReturned = 10;

    public async Task<IReadOnlyList<DuplicateCandidate>> FindAsync(CleanPatient input, Guid? excludePatientId, CancellationToken ct)
    {
        var inputLast = Patient.NormalizeName(input.LastName);
        var inputFirst = Patient.NormalizeName(input.FirstName);
        var inputPhone = PatientInput.DigitsOf(input.Phone);
        var email = input.Email;

        var pool = await db.Patients.AsNoTracking()
            .Where(p => p.DateOfBirth == input.DateOfBirth && (excludePatientId == null || p.Id != excludePatientId))
            .Take(CandidateQueryCap)
            .ToListAsync(ct);

        var found = new List<DuplicateCandidate>();
        foreach (var p in pool)
        {
            var reasons = new List<string>();
            var pLast = Patient.NormalizeName(p.LastName);
            var pFirst = Patient.NormalizeName(p.FirstName);
            var samePhone = inputPhone.Length >= 7 && PatientInput.DigitsOf(p.Phone) == inputPhone;
            var exact = pLast == inputLast && pFirst == inputFirst;

            if (exact) reasons.Add("Same name and date of birth");
            else
            {
                if (pLast == inputLast) reasons.Add("Same last name and date of birth");
                if (pFirst == inputFirst && EditDistance(pLast, inputLast) is > 0 and <= 2) reasons.Add("Same first name and date of birth, similar last name");
                if (samePhone) reasons.Add("Same phone number and date of birth");
                if (email is not null && string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase)) reasons.Add("Same email and date of birth");
            }
            if (reasons.Count == 0) continue;

            found.Add(new DuplicateCandidate(p.Id, p.FirstName, p.MiddleName, p.LastName, p.DateOfBirth.ToString("yyyy-MM-dd"), p.Phone, p.Email, p.City, p.State, p.IsActive, reasons, exact));
        }

        return found.OrderByDescending(c => c.Exact).ThenByDescending(c => c.Reasons.Count).ThenBy(c => c.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.FirstName, StringComparer.OrdinalIgnoreCase).Take(MaxReturned).ToList();
    }

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
