using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients;

/// <summary>The value lists the patient form offers, and the country a new patient starts with.</summary>
public sealed record DemographicOptions(IReadOnlyList<string> SexValues, IReadOnlyList<CodeName> UsStates, IReadOnlyList<CodeName> Countries, string DefaultCountry);

/// <summary>One stored value that is not on its list: which patient, which field, the value as stored.</summary>
public sealed record DemographicReviewItem(Guid PatientId, string DisplayName, long Mrn, string Field, string Value);

/// <summary>The review list: at most <see cref="DemographicsReviewLimit"/> items, and whether there were more.</summary>
public sealed record DemographicReview(IReadOnlyList<DemographicReviewItem> Items, bool Truncated);

/// <summary>
/// ALV-N025 (F-032, F-033; owner decisions DEM-1, DEM-2): what the form needs to offer the lists, and the staff review list - every patient whose stored Sex is not on
/// the Sex list, or whose stored State is not a US code while the Country is US. The migration normalized every recognizable variant (DEM-1), so what is left here is
/// what a person must decide; a patient stays on the list until the value is corrected through the normal edit (which records History). Merged-away records are left
/// out: they never change.
/// </summary>
public sealed class PatientDemographicsReader(AlveraDbContext db)
{
    public const int DemographicsReviewLimit = 500;

    public async Task<DemographicOptions> OptionsAsync(CancellationToken ct) =>
        new(Demographics.SexValues, Demographics.UsStates, Demographics.Countries, (await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct)).DefaultCountry);

    public async Task<DemographicReview> ReviewAsync(CancellationToken ct)
    {
        var sexValues = Demographics.SexValues.ToList();
        var states = Demographics.UsStates.Select(s => s.Code).ToList();
        // a binary collation: an exact value only (the database's own collation would let "female" pass as "Female")
        var rows = await db.Patients.AsNoTracking()
            .Where(p => p.MergedAtUtc == null
                && ((p.Sex != null && !sexValues.Contains(EF.Functions.Collate(p.Sex, "Latin1_General_BIN2")))
                    || (p.Country == Demographics.UnitedStates && !states.Contains(EF.Functions.Collate(p.State, "Latin1_General_BIN2")))))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Id)
            .Take(DemographicsReviewLimit + 1)
            .Select(p => new { p.Id, p.FirstName, p.MiddleName, p.LastName, p.Mrn, p.Sex, p.State, p.Country })
            .ToListAsync(ct);

        var items = new List<DemographicReviewItem>();
        foreach (var p in rows.Take(DemographicsReviewLimit))
        {
            var name = string.Join(" ", new[] { p.FirstName, p.MiddleName, p.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (p.Sex is not null && !sexValues.Contains(p.Sex, StringComparer.Ordinal)) items.Add(new(p.Id, name, p.Mrn, "sex", p.Sex));
            if (p.Country == Demographics.UnitedStates && !states.Contains(p.State, StringComparer.Ordinal)) items.Add(new(p.Id, name, p.Mrn, "state", p.State));
        }
        return new DemographicReview(items, rows.Count > DemographicsReviewLimit);
    }
}
