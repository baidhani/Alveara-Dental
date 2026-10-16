using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Prescribing;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-016-C01: the model for the prescription workflow (drafts, the details finalization adds, and the events that end a prescription). As for STORY-016, the database itself refuses what
/// <see cref="PrescriptionWorkflowRules"/> refuses, so a bug or a hand-written script cannot store an incorrect draft or finalized prescription. Triggers (created in the migration; 51175 to 51181, 51182 and up are
/// free) keep the history safe: a draft is never deleted and a finalized or discarded draft never changes (51175, 51176), a draft's patient and creation never change (51177), a prescription's details never change
/// (51178), a prescription's amendment must be for the same patient (51179), its events are append-only (51180) and a supersession must name a prescription of the same patient (51181).
/// </summary>
internal static class PrescriptionWorkflowModel
{
    private static string QuantityOk(string value, string unit) =>
        $"[{value}] > 0 AND [{value}] <= {PrescriptionWorkflowRules.QuantityTypingCeiling.ToString(System.Globalization.CultureInfo.InvariantCulture)} AND [{unit}] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(PrescriptionWorkflowRules.QuantityUnits)})";

    private static string RefillsOk(string column) => $"[{column}] >= 0 AND [{column}] <= {PrescriptionWorkflowRules.RefillsMax}";

    private static string Hex(string column) => $"[{column}] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([{column}]) = 64";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PrescriptionDraft>(e =>
        {
            e.Property(x => x.Medication).HasMaxLength(PrescriptionRules.MedicationMax);
            e.Property(x => x.DoseValue).HasPrecision(9, PrescriptionRules.DoseDecimals);
            e.Property(x => x.DoseUnit).HasMaxLength(10);
            e.Property(x => x.Frequency).HasMaxLength(20);
            e.Property(x => x.Instructions).HasMaxLength(PrescriptionWorkflowRules.InstructionsMax);
            e.Property(x => x.QuantityValue).HasPrecision(9, PrescriptionWorkflowRules.QuantityDecimals);
            e.Property(x => x.QuantityUnit).HasMaxLength(10);
            e.Property(x => x.ReviewNote).HasMaxLength(PrescriptionRules.ReviewNoteMax);
            e.Property(x => x.SafetyFingerprint).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.DiscardedReason).HasMaxLength(PrescriptionRules.ReviewNoteMax);
            e.Property(x => x.CreateKey).HasMaxLength(PrescriptionRules.KeyMax);
            e.Property(x => x.CreateHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Prescription>().WithMany().HasForeignKey(x => x.SupersedesPrescriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Prescription>().WithMany().HasForeignKey(x => x.FinalizedPrescriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Status, x.UpdatedAtUtc });
            e.HasIndex(x => new { x.PatientId, x.CreateKey }).IsUnique();                  // a retried or double-clicked create with the same key can only ever make one draft
            e.HasIndex(x => x.SupersedesPrescriptionId).IsUnique().HasFilter("[SupersedesPrescriptionId] IS NOT NULL AND [Status] = 'Draft'");   // one open amendment per prescription
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PrescriptionDrafts_NoDelete");
                t.HasTrigger("TR_PrescriptionDrafts_Guard");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Key", $"LEN(LTRIM(RTRIM([CreateKey]))) > 0 AND {PrescriptionModel.NoControl("CreateKey")}");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Hash", Hex("CreateHash"));
                t.HasCheckConstraint("CK_PrescriptionDrafts_Fingerprint", Hex("SafetyFingerprint"));
                t.HasCheckConstraint("CK_PrescriptionDrafts_Medication", $"LEN(LTRIM(RTRIM([Medication]))) >= {PrescriptionRules.MedicationMin} AND {PrescriptionModel.NoControl("Medication")}");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Unit", $"[DoseUnit] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(PrescriptionRules.DoseUnits)})");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Frequency", $"[Frequency] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(PrescriptionRules.Frequencies)})");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Dose", $"[DoseValue] > 0 AND {PrescriptionModel.DoseWithinCeiling()}");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Instructions", "[Instructions] IS NULL OR LEN(LTRIM(RTRIM([Instructions]))) > 0");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Quantity", $"([QuantityValue] IS NULL AND [QuantityUnit] IS NULL) OR ([QuantityValue] IS NOT NULL AND [QuantityUnit] IS NOT NULL AND {QuantityOk("QuantityValue", "QuantityUnit")})");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Refills", $"[Refills] IS NULL OR ({RefillsOk("Refills")})");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Review", "[ReviewNote] IS NULL OR LEN(LTRIM(RTRIM([ReviewNote]))) > 0");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Status", $"[Status] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(DraftStatuses.All)})");
                // a draft that was finalized says which prescription it became, a discarded one may say why, and nothing else carries either
                t.HasCheckConstraint("CK_PrescriptionDrafts_Outcome",
                    $"(([Status] = '{DraftStatuses.Finalized}' AND [FinalizedPrescriptionId] IS NOT NULL) OR ([Status] <> '{DraftStatuses.Finalized}' AND [FinalizedPrescriptionId] IS NULL)) " +
                    $"AND ([DiscardedReason] IS NULL OR ([Status] = '{DraftStatuses.Discarded}' AND LEN(LTRIM(RTRIM([DiscardedReason]))) > 0))");
                t.HasCheckConstraint("CK_PrescriptionDrafts_Times", "[CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01' AND [UpdatedAtUtc] >= [CreatedAtUtc]");
            });
        });

        modelBuilder.Entity<PrescriptionDetail>(e =>
        {
            e.HasKey(x => x.PrescriptionId);
            e.Property(x => x.Instructions).HasMaxLength(PrescriptionWorkflowRules.InstructionsMax);
            e.Property(x => x.QuantityValue).HasPrecision(9, PrescriptionWorkflowRules.QuantityDecimals);
            e.Property(x => x.QuantityUnit).HasMaxLength(10);
            e.Property(x => x.SafetyFingerprint).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.HasOne<Prescription>().WithOne().HasForeignKey<PrescriptionDetail>(x => x.PrescriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PrescriptionDraft>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Prescription>().WithMany().HasForeignKey(x => x.SupersedesPrescriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.FinalizedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DraftId).IsUnique().HasFilter("[DraftId] IS NOT NULL");                                      // a draft becomes one prescription
            e.HasIndex(x => x.SupersedesPrescriptionId).IsUnique().HasFilter("[SupersedesPrescriptionId] IS NOT NULL");    // a prescription is amended by one successor
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PrescriptionDetails_Immutable");
                t.HasTrigger("TR_PrescriptionDetails_Links");
                t.HasCheckConstraint("CK_PrescriptionDetails_Instructions", "LEN(LTRIM(RTRIM([Instructions]))) > 0");
                t.HasCheckConstraint("CK_PrescriptionDetails_Quantity", QuantityOk("QuantityValue", "QuantityUnit"));
                t.HasCheckConstraint("CK_PrescriptionDetails_Refills", RefillsOk("Refills"));
                t.HasCheckConstraint("CK_PrescriptionDetails_Fingerprint", Hex("SafetyFingerprint"));
                t.HasCheckConstraint("CK_PrescriptionDetails_Supersedes", "[SupersedesPrescriptionId] IS NULL OR [SupersedesPrescriptionId] <> [PrescriptionId]");
                t.HasCheckConstraint("CK_PrescriptionDetails_Finalized", "[FinalizedAtUtc] >= '2000-01-01' AND [FinalizedAtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<PrescriptionEvent>(e =>
        {
            e.Property(x => x.EventType).HasMaxLength(14);
            e.Property(x => x.Reason).HasMaxLength(PrescriptionRules.ReviewNoteMax);
            e.HasOne<Prescription>().WithMany().HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Prescription>().WithMany().HasForeignKey(x => x.SupersededByPrescriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PrescriptionId, x.OccurredAtUtc });
            // a prescription is finalized once and ends at most once: Active can only become one ended state
            e.HasIndex(x => x.PrescriptionId, "IX_PrescriptionEvents_OneFinalized").IsUnique().HasFilter($"[EventType] = '{PrescriptionEventTypes.Finalized}'");
            e.HasIndex(x => x.PrescriptionId, "IX_PrescriptionEvents_OneEnding").IsUnique().HasFilter($"[EventType] IN ({PrescriptionModel.In(PrescriptionEventTypes.Ending)})");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PrescriptionEvents_Immutable");
                t.HasTrigger("TR_PrescriptionEvents_Links");
                t.HasCheckConstraint("CK_PrescriptionEvents_Type", $"[EventType] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(PrescriptionEventTypes.All)})");
                // cancelling or discontinuing needs a reason; a supersession names its successor; nothing else carries either
                t.HasCheckConstraint("CK_PrescriptionEvents_Reason",
                    $"(([EventType] IN ('{PrescriptionEventTypes.Cancelled}','{PrescriptionEventTypes.Discontinued}') AND [Reason] IS NOT NULL) " +
                    $"OR ([EventType] NOT IN ('{PrescriptionEventTypes.Cancelled}','{PrescriptionEventTypes.Discontinued}') AND [Reason] IS NULL)) AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)");
                t.HasCheckConstraint("CK_PrescriptionEvents_Superseded",
                    $"(([EventType] = '{PrescriptionEventTypes.Superseded}' AND [SupersededByPrescriptionId] IS NOT NULL) OR ([EventType] <> '{PrescriptionEventTypes.Superseded}' AND [SupersededByPrescriptionId] IS NULL)) " +
                    "AND ([SupersededByPrescriptionId] IS NULL OR [SupersededByPrescriptionId] <> [PrescriptionId])");
                t.HasCheckConstraint("CK_PrescriptionEvents_Time", "[OccurredAtUtc] >= '2000-01-01' AND [OccurredAtUtc] < '2200-01-01'");
            });
        });
    }
}
