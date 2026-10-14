using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Prescribing;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-016: the model for prescriptions. The database itself refuses what <see cref="PrescriptionRules"/> refuses, so a bug in the service or a hand-written script cannot store an incorrect entry: a blank or
/// control-character medication, a unit or frequency that is not on the closed lists, a dose that is not positive or is above the typing-error ceiling of its unit, an allergy result that disagrees with the
/// list state it was made from, and an entry that needed the prescriber's review but carries none. A trigger (created in the migration, error 51174; 51175 and up are free) makes a prescription append-only.
/// </summary>
internal static class PrescriptionModel
{
    private static string In(IEnumerable<string> values) => string.Join(",", values.Select(v => $"'{v}'"));

    private static string NoControl(string column) => $"PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [{column}] COLLATE Latin1_General_BIN2) = 0";

    private static string DoseWithinCeiling() =>
        "(" + string.Join(" OR ", PrescriptionRules.TypingCeilings.Select(c =>
            $"([DoseUnit] COLLATE Latin1_General_CS_AS = '{c.Key}' AND [DoseValue] <= {c.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)})")) + ")";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Prescription>(e =>
        {
            e.Property(x => x.Medication).HasMaxLength(PrescriptionRules.MedicationMax);
            e.Property(x => x.DoseValue).HasPrecision(9, PrescriptionRules.DoseDecimals);
            e.Property(x => x.DoseUnit).HasMaxLength(10);
            e.Property(x => x.Frequency).HasMaxLength(20);
            e.Property(x => x.AllergyOutcome).HasMaxLength(24);
            e.Property(x => x.AllergyListState).HasMaxLength(16);
            e.Property(x => x.AllergyMatches).HasMaxLength(1000);
            e.Property(x => x.ReviewNote).HasMaxLength(PrescriptionRules.ReviewNoteMax);
            e.Property(x => x.IdempotencyKey).HasMaxLength(PrescriptionRules.KeyMax);
            e.Property(x => x.RequestHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.PrescriberUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.CreatedAtUtc });
            // a retried or double-clicked save with the same key can only ever create one prescription
            e.HasIndex(x => new { x.PatientId, x.IdempotencyKey }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_Prescriptions_Immutable");
                t.HasCheckConstraint("CK_Prescriptions_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {NoControl("IdempotencyKey")}");
                t.HasCheckConstraint("CK_Prescriptions_Hash", "[RequestHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64");
                t.HasCheckConstraint("CK_Prescriptions_Medication", $"LEN(LTRIM(RTRIM([Medication]))) >= {PrescriptionRules.MedicationMin} AND {NoControl("Medication")}");
                t.HasCheckConstraint("CK_Prescriptions_Unit", $"[DoseUnit] COLLATE Latin1_General_CS_AS IN ({In(PrescriptionRules.DoseUnits)})");
                t.HasCheckConstraint("CK_Prescriptions_Frequency", $"[Frequency] COLLATE Latin1_General_CS_AS IN ({In(PrescriptionRules.Frequencies)})");
                t.HasCheckConstraint("CK_Prescriptions_Dose", $"[DoseValue] > 0 AND {DoseWithinCeiling()}");
                t.HasCheckConstraint("CK_Prescriptions_Allergy",
                    $"[AllergyOutcome] COLLATE Latin1_General_CS_AS IN ({In(new[] { AllergyOutcomes.RecordedAllergyMatch, AllergyOutcomes.NoRecordedMatch, AllergyOutcomes.NotEstablished })}) " +
                    $"AND [AllergyListState] COLLATE Latin1_General_CS_AS IN ({In(new[] { AllergyListStates.Established, AllergyListStates.Unconfirmed, AllergyListStates.NotEstablished })}) " +
                    $"AND (([AllergyOutcome] = '{AllergyOutcomes.RecordedAllergyMatch}' AND [AllergyMatches] IS NOT NULL AND LEN(LTRIM(RTRIM([AllergyMatches]))) > 0) " +
                    $"OR ([AllergyOutcome] = '{AllergyOutcomes.NoRecordedMatch}' AND [AllergyListState] = '{AllergyListStates.Established}' AND [AllergyMatches] IS NULL) " +
                    $"OR ([AllergyOutcome] = '{AllergyOutcomes.NotEstablished}' AND [AllergyListState] <> '{AllergyListStates.Established}' AND [AllergyMatches] IS NULL))");
                t.HasCheckConstraint("CK_Prescriptions_Review", "[ReviewNote] IS NULL OR LEN(LTRIM(RTRIM([ReviewNote]))) > 0");
                // an entry that needed the prescriber's review (a match, or an allergy list that is not established) must carry it
                t.HasCheckConstraint("CK_Prescriptions_ReviewRequired", $"[AllergyOutcome] = '{AllergyOutcomes.NoRecordedMatch}' OR [ReviewNote] IS NOT NULL");
                t.HasCheckConstraint("CK_Prescriptions_Created", "[CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01'");
            });
        });
    }
}
