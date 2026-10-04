using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Safety;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-N011: the model for patient-safety alerts, their acknowledgements, clearances and the append-only histories. Check constraints are case-sensitive on purpose (the default
/// collation would accept 'resolved'); the triggers (created in the migration) refuse deleting alerts and clearances and any edit of a history or an acknowledgement. The
/// "resolved needs who, when and why" stamps are checked in the database as well as the service.
/// </summary>
internal static class SafetyModel
{
    private const string Sev = "'Critical','High','Moderate','Low'";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SafetyAlert>(e =>
        {
            e.Property(x => x.Category).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.Property(x => x.Severity).HasMaxLength(10);
            e.Property(x => x.SourceNote).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.ResolutionReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ClinicalRecordItem>().WithMany().HasForeignKey(x => x.SourceItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Status });
            // one ACTIVE alert per category and title per patient (a resolved one can be entered again, or reopened if no active twin exists)
            e.HasIndex(x => new { x.PatientId, x.Category, x.Title }).IsUnique().HasFilter("[Status] = 'Active'");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_SafetyAlerts_NoDelete");
                t.HasCheckConstraint("CK_SafetyAlerts_Category", "[Category] COLLATE Latin1_General_CS_AS IN ('Condition','Pregnancy','Anticoagulant','AdverseReaction','Custom')");
                t.HasCheckConstraint("CK_SafetyAlerts_Severity", $"[Severity] COLLATE Latin1_General_CS_AS IN ({Sev})");
                t.HasCheckConstraint("CK_SafetyAlerts_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Resolved')");
                t.HasCheckConstraint("CK_SafetyAlerts_TitleNotBlank", "LEN(LTRIM(RTRIM([Title]))) > 0");
                t.HasCheckConstraint("CK_SafetyAlerts_SourceNotBlank", "LEN(LTRIM(RTRIM([SourceNote]))) > 0");
                t.HasCheckConstraint("CK_SafetyAlerts_ResolvedStamp",
                    "([Status] = 'Resolved' AND [ResolvedAtUtc] IS NOT NULL AND [ResolvedByUserId] IS NOT NULL AND [ResolutionReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ResolutionReason]))) > 0) " +
                    "OR ([Status] = 'Active' AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL AND [ResolutionReason] IS NULL)");
            });
        });
        modelBuilder.Entity<SafetyAlertVersion>(e =>
        {
            e.Property(x => x.ChangeType).HasMaxLength(10);
            e.Property(x => x.Category).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.Property(x => x.Severity).HasMaxLength(10);
            e.Property(x => x.SourceNote).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<SafetyAlert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AlertId, x.VersionNumber }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_SafetyAlertVersions_Immutable");
                t.HasCheckConstraint("CK_SafetyAlertVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','Changed','Resolved','Reopened')");
            });
        });
        modelBuilder.Entity<SafetyAlertAcknowledgement>(e =>
        {
            e.HasOne<SafetyAlert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AlertId, x.UserId, x.Revision }).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.ToTable(t => t.HasTrigger("TR_SafetyAlertAcks_Immutable"));
        });

        modelBuilder.Entity<Clearance>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.ReasonKey).HasMaxLength(64);
            e.Property(x => x.RequestedFrom).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.DocumentReference).HasMaxLength(300);
            e.Property(x => x.ReceivedNote).HasMaxLength(500);
            e.Property(x => x.ClosingReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Status });
            // one OPEN clearance of a kind per reason per patient: a retried or simultaneous request cannot open a second one
            e.HasIndex(x => new { x.PatientId, x.Kind, x.ReasonKey }).IsUnique().HasFilter("[Status] IN ('Requested','Received')");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_Clearances_NoDelete");
                t.HasCheckConstraint("CK_Clearances_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('Medical','Dental')");
                t.HasCheckConstraint("CK_Clearances_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Requested','Received','Resolved','Cancelled')");
                t.HasCheckConstraint("CK_Clearances_ReasonNotBlank", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                // received (or resolved) means someone received it; closed (resolved or cancelled) means who, when and why - and a clearance cannot be resolved without being received
                t.HasCheckConstraint("CK_Clearances_Stamps",
                    "([Status] = 'Requested' AND [ReceivedAtUtc] IS NULL AND [ClosedAtUtc] IS NULL) " +
                    "OR ([Status] = 'Received' AND [ReceivedAtUtc] IS NOT NULL AND [ReceivedByUserId] IS NOT NULL AND [ClosedAtUtc] IS NULL) " +
                    "OR ([Status] = 'Resolved' AND [ReceivedAtUtc] IS NOT NULL AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [ClosingReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ClosingReason]))) > 0) " +
                    "OR ([Status] = 'Cancelled' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [ClosingReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ClosingReason]))) > 0)");
            });
        });
        modelBuilder.Entity<ClearanceVersion>(e =>
        {
            e.Property(x => x.ChangeType).HasMaxLength(16);
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.RequestedFrom).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.DocumentReference).HasMaxLength(300);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne<Clearance>().WithMany().HasForeignKey(x => x.ClearanceId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ClearanceId, x.VersionNumber }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ClearanceVersions_Immutable");
                t.HasCheckConstraint("CK_ClearanceVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Requested','Received','DocumentAttached','Resolved','Cancelled')");
            });
        });
    }
}
