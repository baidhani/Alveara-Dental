using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients;

public record PatientSummary(Guid Id, string FirstName, string? MiddleName, string LastName, string DateOfBirth, int Age, string? Sex, string Phone, string City, string State, bool IsActive);
public record PatientLink(Guid Id, string DisplayName, bool IsActive);
public record HouseholdMember(Guid Id, string DisplayName, string? Relationship, bool IsActive);
public record PatientDetail(
    Patient Patient, int Age, PatientLink? Guarantor, IReadOnlyList<PatientLink> GuaranteeFor,
    Guid? HouseholdId, IReadOnlyList<HouseholdMember> HouseholdMembers);
public record PatientHistoryRow(Guid Id, string ChangedAtUtc, Guid? ChangedByUserId, string ChangeType, string FieldName, string? OldValue, string? NewValue);

/// <summary>
/// ALV-003-C01: read side - search, the identity summary/detail the shared patient header shows, and edit history.
/// Searching is bounded (never more than <see cref="MaxResults"/> rows) and understands what front desk actually types:
/// a birth date (yyyy-MM-dd), a phone number (any formatting; matched on its digits), or name words (each must start a
/// first, middle or last name). Inactive patients are hidden unless asked for, never lost.
/// </summary>
public class PatientDirectory(AlveraDbContext db, IPracticeClock clock)
{
    public const int MaxResults = 50;

    public async Task<IReadOnlyList<PatientSummary>> SearchAsync(string? query, bool includeInactive, int take, CancellationToken ct)
    {
        var limit = Math.Clamp(take, 1, MaxResults);
        var patients = db.Patients.AsNoTracking().AsQueryable();
        if (!includeInactive) patients = patients.Where(p => p.IsActive);

        var q = query?.Trim() ?? "";
        if (q.Length > 0)
        {
            if (DateOnly.TryParseExact(q, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
            {
                patients = patients.Where(p => p.DateOfBirth == dob);
            }
            else if (q.All(c => char.IsDigit(c) || " ()-.+".Contains(c)) && PatientInput.DigitsOf(q).Length >= 3)
            {
                var digits = PatientInput.DigitsOf(q);
                patients = patients.Where(p => p.Phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "").Replace(".", "").Replace("+", "").Contains(digits));
            }
            else
            {
                foreach (var word in q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(4))
                {
                    var w = word;
                    patients = patients.Where(p => p.FirstName.StartsWith(w) || p.LastName.StartsWith(w) || (p.MiddleName != null && p.MiddleName.StartsWith(w)));
                }
            }
        }

        var rows = await patients.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.DateOfBirth).Take(limit).ToListAsync(ct);
        return rows.Select(ToSummary).ToList();
    }

    public async Task<PatientDetail?> GetDetailAsync(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (patient is null) return null;

        PatientLink? guarantor = null;
        if (patient.GuarantorPatientId is { } gid)
        {
            var g = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == gid, ct);
            if (g is not null) guarantor = new PatientLink(g.Id, DisplayName(g), g.IsActive);
        }

        var guaranteeFor = await db.Patients.AsNoTracking().Where(p => p.GuarantorPatientId == id).OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .ToListAsync(ct);

        var members = new List<HouseholdMember>();
        if (patient.HouseholdId is { } hid)
        {
            var rows = await db.Patients.AsNoTracking().Where(p => p.HouseholdId == hid).OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToListAsync(ct);
            members = rows.Select(m => new HouseholdMember(m.Id, DisplayName(m), m.HouseholdRelationship, m.IsActive)).ToList();
        }

        return new PatientDetail(patient, AgeOn(patient.DateOfBirth), guarantor,
            guaranteeFor.Select(p => new PatientLink(p.Id, DisplayName(p), p.IsActive)).ToList(), patient.HouseholdId, members);
    }

    public async Task<IReadOnlyList<PatientHistoryRow>?> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == id, ct)) return null;
        var rows = await db.PatientHistory.AsNoTracking().Where(h => h.PatientId == id).OrderByDescending(h => h.ChangedAtUtc).Take(200).ToListAsync(ct);
        return rows.Select(h => new PatientHistoryRow(h.Id, h.ChangedAtUtc.ToString("O"), h.ChangedByUserId, h.ChangeType, h.FieldName, h.OldValue, h.NewValue)).ToList();
    }

    private PatientSummary ToSummary(Patient p) =>
        new(p.Id, p.FirstName, p.MiddleName, p.LastName, p.DateOfBirth.ToString("yyyy-MM-dd"), AgeOn(p.DateOfBirth), p.Sex, p.Phone, p.City, p.State, p.IsActive);

    public static string DisplayName(Patient p) => $"{p.FirstName} {p.LastName}";

    /// <summary>Age in whole years on the practice's calendar date (never the server's time zone).</summary>
    private int AgeOn(DateOnly dob)
    {
        var today = DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).DateTime);
        var age = today.Year - dob.Year;
        if (today < dob.AddYears(age)) age--;
        return Math.Max(age, 0);
    }
}
