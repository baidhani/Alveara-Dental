using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-015-C01: the model for plan revisions, phases, per-procedure acceptance, scheduling and completion state, and the resolution of old diagnosis plan references. Everything here is append-only (a trigger,
/// created in the migration, refuses any edit or delete). Beyond the checks on each row, triggers keep the rows consistent with each other (the patient and plan on a row are those of the row it belongs to;
/// a procedure's state is only recorded for a procedure of that plan that has not been withdrawn; Completed is final; a diagnosis is only resolved to a plan of its own patient, with the text it holds).
/// Rows that depend on each other are linked by foreign keys, so the database never needs a trigger that requires an earlier row of the same save.
/// </summary>
internal static class TreatmentPlanRevisionModel
{
    private static string In(IEnumerable<string> values) => TreatmentPlanModel.In(values);
    private static string NoControl(string column) => TreatmentPlanModel.NoControl(column);
    private static string NotBlank(string column) => $"[{column}] IS NULL OR LEN(LTRIM(RTRIM([{column}]))) > 0";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TreatmentPlanRevision>(e =>
        {
            e.Property(x => x.ChangeType).HasMaxLength(TreatmentPlanRules.ChangeTypeMax);
            e.Property(x => x.Title).HasMaxLength(TreatmentPlanRules.TitleMax);
            e.Property(x => x.ClinicalRationale).HasMaxLength(TreatmentPlanRules.TextMax);
            e.Property(x => x.PatientExplanation).HasMaxLength(TreatmentPlanRules.TextMax);
            e.Property(x => x.Reason).HasMaxLength(TreatmentPlanRules.ReasonMax);
            e.HasOne<TreatmentPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PlanId, x.RevisionNumber }).IsUnique();
            e.HasIndex(x => new { x.PlanId, x.EventNumber }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_TreatmentPlanRevisions_Immutable");
                t.HasTrigger("TR_TreatmentPlanRevisions_Links");
                t.HasCheckConstraint("CK_TreatmentPlanRevisions_Number", "[RevisionNumber] >= 1 AND [EventNumber] >= 1");
                t.HasCheckConstraint("CK_TreatmentPlanRevisions_ChangeType", $"[ChangeType] COLLATE Latin1_General_CS_AS IN ({In(TreatmentPlanChanges.MakeARevision)})");
                t.HasCheckConstraint("CK_TreatmentPlanRevisions_Title", $"LEN(LTRIM(RTRIM([Title]))) > 0 AND {NoControl("Title")}");
                t.HasCheckConstraint("CK_TreatmentPlanRevisions_Text", $"({NotBlank("ClinicalRationale")}) AND ({NotBlank("PatientExplanation")}) AND ({NotBlank("Reason")})");
            });
        });

        modelBuilder.Entity<TreatmentPlanRevisionPhase>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(TreatmentPlanRules.PhaseNameMax);
            e.Property(x => x.PlannedVisit).HasMaxLength(TreatmentPlanRules.PlannedVisitMax);
            e.HasOne<TreatmentPlanRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RevisionId, x.PhaseNumber }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_TreatmentPlanRevisionPhases_Immutable");
                t.HasTrigger("TR_TreatmentPlanRevisionPhases_Links");
                t.HasCheckConstraint("CK_TreatmentPlanRevisionPhases_Number", $"[PhaseNumber] >= 1 AND [PhaseNumber] <= {TreatmentPlanRules.PhasesMax}");
                t.HasCheckConstraint("CK_TreatmentPlanRevisionPhases_Name", $"LEN(LTRIM(RTRIM([Name]))) > 0 AND {NoControl("Name")}");
                t.HasCheckConstraint("CK_TreatmentPlanRevisionPhases_Visit", $"[PlannedVisit] IS NULL OR (LEN(LTRIM(RTRIM([PlannedVisit]))) > 0 AND {NoControl("PlannedVisit")})");
            });
        });

        modelBuilder.Entity<TreatmentPlanRevisionItem>(e =>
        {
            e.HasOne<TreatmentPlanRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TreatmentPlanItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            // a procedure can only sit in a phase that this same revision has (a null phase means not yet placed, and SQL Server does not check a foreign key with a null part)
            e.HasOne<TreatmentPlanRevisionPhase>().WithMany().HasForeignKey(x => new { x.RevisionId, x.PhaseNumber }).HasPrincipalKey(p => new { p.RevisionId, p.PhaseNumber }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RevisionId, x.ItemId }).IsUnique();
            e.HasIndex(x => x.ItemId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_TreatmentPlanRevisionItems_Immutable");
                t.HasTrigger("TR_TreatmentPlanRevisionItems_Links");
                t.HasCheckConstraint("CK_TreatmentPlanRevisionItems_Phase", "[PhaseNumber] IS NULL OR [PhaseNumber] >= 1");
            });
        });

        modelBuilder.Entity<TreatmentPlanItemState>(e =>
        {
            e.Property(x => x.Acceptance).HasMaxLength(8);
            e.Property(x => x.Scheduling).HasMaxLength(11);
            e.Property(x => x.Completion).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(TreatmentPlanRules.ReasonMax);
            e.Property(x => x.DecidedByName).HasMaxLength(TreatmentPlanRules.DecidedByMax);
            e.HasOne<TreatmentPlanItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ItemId, x.Sequence }).IsUnique();
            e.HasIndex(x => x.PlanId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_TreatmentPlanItemStates_Immutable");
                t.HasTrigger("TR_TreatmentPlanItemStates_Links");
                t.HasCheckConstraint("CK_TreatmentPlanItemStates_Number", "[Sequence] >= 1 AND [RevisionNumber] >= 1");
                t.HasCheckConstraint("CK_TreatmentPlanItemStates_Acceptance", $"[Acceptance] COLLATE Latin1_General_CS_AS IN ({In(ItemAcceptance.All)})");
                t.HasCheckConstraint("CK_TreatmentPlanItemStates_Scheduling", $"[Scheduling] COLLATE Latin1_General_CS_AS IN ({In(ItemScheduling.All)})");
                t.HasCheckConstraint("CK_TreatmentPlanItemStates_Completion", $"[Completion] COLLATE Latin1_General_CS_AS IN ({In(ItemCompletion.All)})");
                t.HasCheckConstraint("CK_TreatmentPlanItemStates_Text", $"({NotBlank("Reason")}) AND ({NotBlank("DecidedByName")})");
                // declining always says why; scheduling or completing needs an accepted procedure; a planned date belongs to a scheduled one
                t.HasCheckConstraint("CK_TreatmentPlanItemStates_Declined", "[Acceptance] <> 'Declined' OR [Reason] IS NOT NULL");
                t.HasCheckConstraint("CK_TreatmentPlanItemStates_Accepted", "([Scheduling] = 'Unscheduled' AND [Completion] = 'NotStarted') OR [Acceptance] = 'Accepted'");
                t.HasCheckConstraint("CK_TreatmentPlanItemStates_PlannedDate", "[PlannedDate] IS NULL OR [Scheduling] = 'Scheduled'");
            });
        });

        modelBuilder.Entity<DiagnosisPlanResolution>(e =>
        {
            e.Property(x => x.ReferenceText).HasMaxLength(DiagnosisRules.ReferenceMax);
            e.Property(x => x.Method).HasMaxLength(20);
            e.HasOne<Diagnosis>().WithMany().HasForeignKey(x => x.DiagnosisId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TreatmentPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            // one resolution per diagnosis: resolving again can only find the one that exists
            e.HasIndex(x => x.DiagnosisId).IsUnique();
            e.HasIndex(x => x.PlanId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_DiagnosisPlanResolutions_Immutable");
                t.HasTrigger("TR_DiagnosisPlanResolutions_Links");
                t.HasCheckConstraint("CK_DiagnosisPlanResolutions_Text", "LEN(LTRIM(RTRIM([ReferenceText]))) > 0");
                t.HasCheckConstraint("CK_DiagnosisPlanResolutions_Method", $"[Method] COLLATE Latin1_General_CS_AS IN ({In(ResolutionMethods.All)})");
            });
        });
    }
}
