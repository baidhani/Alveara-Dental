using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-008-C01: the model for completing a selected set of procedures as one operation: the completion set (the idempotency anchor), the authoritative completed-procedure record and its append-only amendments.
/// The database refuses what the rules refuse (a blank or oversized key, a hash that is not a SHA-256, a chart outcome that does not match its finding, an amendment that corrects nothing or has no reason) and, with
/// triggers created in the migration (error numbers 51144 to 51156), what no single row can show: changing or deleting any of the three tables, a set that is not its plan's patient's, a completed procedure whose plan, patient, set, plan item, catalog
/// version, tooth, surface, encounter or finding do not belong together, one recorded for a plan item that is not completed or is withdrawn, one performed before its plan existed or before the item was accepted,
/// and an amendment that is not the next number or whose encounter is another patient's. Unique indexes allow one completed procedure per plan item, one set per plan and key, and one amendment per procedure and
/// number or key.
/// </summary>
internal static class CompletionModel
{
    private static string In(IEnumerable<string> values) => TreatmentPlanModel.In(values);
    private static string NoControl(string column) => TreatmentPlanModel.NoControl(column);
    private const string HexHash = "LEN([RequestHash]) = 64 AND [RequestHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%'";
    private const string SaneTime = "'2000-01-01'";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompletionSet>(e =>
        {
            e.Property(x => x.IdempotencyKey).HasMaxLength(CompletionLimits.KeyMax);
            e.Property(x => x.RequestHash).HasMaxLength(CompletionLimits.HashLength).IsFixedLength().IsUnicode(false);
            e.Property(x => x.AdoptionReason).HasMaxLength(CompletionLimits.ReasonMax);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TreatmentPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PlanId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => x.CorrelationId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_CompletionSets_Immutable");
                t.HasTrigger("TR_CompletionSets_Links");
                t.HasTrigger("TR_CompletionSets_Adoption");
                t.HasCheckConstraint("CK_CompletionSets_AdoptionReason", $"[AdoptionReason] IS NULL OR (LEN(LTRIM(RTRIM([AdoptionReason]))) > 0 AND {NoControl("AdoptionReason")})");
                t.HasCheckConstraint("CK_CompletionSets_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {NoControl("IdempotencyKey")}");
                t.HasCheckConstraint("CK_CompletionSets_Hash", HexHash);
                t.HasCheckConstraint("CK_CompletionSets_Counts", "[ItemCount] >= 1 AND [PlanRevisionNumber] >= 1");
            });
        });

        modelBuilder.Entity<CompletedProcedure>(e =>
        {
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Surface).HasMaxLength(1).IsFixedLength();
            e.Property(x => x.ChartOutcome).HasMaxLength(16);
            e.HasOne<CompletionSet>().WithMany().HasForeignKey(x => x.SetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TreatmentPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TreatmentPlanItem>().WithMany().HasForeignKey(x => x.PlanItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProcedureVersion>().WithMany().HasForeignKey(x => x.ProcedureVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProviderProfile>().WithMany().HasForeignKey(x => x.ProviderProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ToothFinding>().WithMany().HasForeignKey(x => x.ToothFindingId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PlanItemId).IsUnique();                                        // a procedure is completed once
            e.HasIndex(x => x.SetId);
            e.HasIndex(x => new { x.PatientId, x.PerformedAtUtc });
            e.ToTable(t =>
            {
                t.HasTrigger("TR_CompletedProcedures_Immutable");
                t.HasTrigger("TR_CompletedProcedures_Links");
                t.HasTrigger("TR_CompletedProcedures_ChartEvidence");
                t.HasCheckConstraint("CK_CompletedProcedures_Chart",
                    $"[ChartOutcome] COLLATE Latin1_General_CS_AS IN ({In(ChartOutcomes.All)}) AND (([ChartOutcome] = '{ChartOutcomes.NotApplicable}' AND [ToothFindingId] IS NULL) OR ([ChartOutcome] <> '{ChartOutcomes.NotApplicable}' AND [ToothFindingId] IS NOT NULL))");
                t.HasCheckConstraint("CK_CompletedProcedures_ChartVersion", "[ToothFindingVersionNumber] IS NULL OR ([ToothFindingId] IS NOT NULL AND [ToothFindingVersionNumber] >= 1)");
                t.HasCheckConstraint("CK_CompletedProcedures_Revision", "[PlanRevisionNumber] >= 1");
                t.HasCheckConstraint("CK_CompletedProcedures_Surface", "[Surface] IS NULL OR [ToothKey] IS NOT NULL");
                t.HasCheckConstraint("CK_CompletedProcedures_Performed", $"[PerformedAtUtc] >= {SaneTime} AND [PerformedAtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<CompletedProcedureAmendment>(e =>
        {
            e.Property(x => x.IdempotencyKey).HasMaxLength(CompletionLimits.KeyMax);
            e.Property(x => x.RequestHash).HasMaxLength(CompletionLimits.HashLength).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Reason).HasMaxLength(CompletionLimits.ReasonMax);
            e.HasOne<CompletedProcedure>().WithMany().HasForeignKey(x => x.CompletedProcedureId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProviderProfile>().WithMany().HasForeignKey(x => x.ProviderProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CompletedProcedureId, x.AmendmentNumber }).IsUnique();
            e.HasIndex(x => new { x.CompletedProcedureId, x.IdempotencyKey }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_CompletedProcedureAmendments_Immutable");
                t.HasTrigger("TR_CompletedProcedureAmendments_Links");
                t.HasCheckConstraint("CK_CompletedProcedureAmendments_Number", "[AmendmentNumber] >= 1");
                t.HasCheckConstraint("CK_CompletedProcedureAmendments_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {NoControl("IdempotencyKey")}");
                t.HasCheckConstraint("CK_CompletedProcedureAmendments_Hash", HexHash);
                t.HasCheckConstraint("CK_CompletedProcedureAmendments_Reason", $"LEN(LTRIM(RTRIM([Reason]))) > 0 AND {NoControl("Reason")}");
                t.HasCheckConstraint("CK_CompletedProcedureAmendments_Changes", "[ProviderProfileId] IS NOT NULL OR [PerformedAtUtc] IS NOT NULL OR [EncounterId] IS NOT NULL");
                t.HasCheckConstraint("CK_CompletedProcedureAmendments_Performed", $"[PerformedAtUtc] IS NULL OR ([PerformedAtUtc] >= {SaneTime} AND [PerformedAtUtc] < '2200-01-01')");
            });
        });
    }
}
