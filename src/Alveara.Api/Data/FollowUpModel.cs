using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-009: the model for follow-up tasks, their append-only history and the record of delivered reminders (docs/design/FOLLOW_UP_TASKS_DESIGN.md). As elsewhere, the database refuses by itself what the rules refuse.
/// Triggers (created in the migration; errors 51209 to 51213): a task event is never updated or deleted (51209); a task is never deleted, only an open task can change, and only to completed or cancelled with
/// who and when, and its patient, creator and creation time never change (51210); a task is assigned to an enabled account and a recall belongs to the same patient as the task it follows (51211); a reminder
/// delivery is never updated or deleted (51212); the migration refuses to roll back while a task exists (51213).
/// </summary>
internal static class FollowUpModel
{
    private const string Utc = "'2000-01-01'";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FollowUpTask>(e =>
        {
            e.ToTable("FollowUpTasks");
            e.Property(x => x.Title).HasMaxLength(FollowUpRules.TitleMaxLength);
            e.Property(x => x.Note).HasMaxLength(FollowUpRules.NoteMaxLength);
            e.Property(x => x.ClosureNote).HasMaxLength(FollowUpRules.NoteMaxLength);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FollowUpTask>().WithMany().HasForeignKey(x => x.RecallOfTaskId).OnDelete(DeleteBehavior.Restrict);
            // a task creates at most one recall, however many times its completion is repeated
            e.HasIndex(x => x.RecallOfTaskId).IsUnique().HasFilter("[RecallOfTaskId] IS NOT NULL");
            e.HasIndex(x => new { x.PatientId, x.Status, x.DueAtUtc });
            // "my follow-ups" and the reminder scan both read open tasks by due time
            e.HasIndex(x => new { x.AssigneeUserId, x.Status, x.DueAtUtc });
            e.HasIndex(x => new { x.Status, x.DueAtUtc });
            e.ToTable(t =>
            {
                t.HasTrigger("TR_FollowUpTasks_Guard");
                t.HasTrigger("TR_FollowUpTasks_Links");
                t.HasCheckConstraint("CK_FollowUpTasks_Status", $"[Status] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(FollowUpStatuses.All)})");
                t.HasCheckConstraint("CK_FollowUpTasks_Title", $"{NotificationModel.NotBlank("Title")} AND {NotificationModel.NoNul("Title")} AND {PrescriptionModel.NoControl("Title")}");
                t.HasCheckConstraint("CK_FollowUpTasks_Note", $"[Note] IS NULL OR ({NotificationModel.NotBlank("Note")} AND {NotificationModel.NoNul("Note")} AND {NotificationModel.BodyChars("Note")})");
                t.HasCheckConstraint("CK_FollowUpTasks_ClosureNote", $"[ClosureNote] IS NULL OR ({NotificationModel.NotBlank("ClosureNote")} AND {NotificationModel.NoNul("ClosureNote")} AND {NotificationModel.BodyChars("ClosureNote")})");
                t.HasCheckConstraint("CK_FollowUpTasks_Interval", $"[RecallIntervalDays] IS NULL OR ([RecallIntervalDays] >= 1 AND [RecallIntervalDays] <= {FollowUpRules.MaxRecallIntervalDays})");
                t.HasCheckConstraint("CK_FollowUpTasks_Times", $"[DueAtUtc] >= {Utc} AND [DueAtUtc] < '2200-01-01' AND [CreatedAtUtc] >= {Utc} AND [CreatedAtUtc] < '2200-01-01' AND ([ClosedAtUtc] IS NULL OR [ClosedAtUtc] >= [CreatedAtUtc])");
                // an open task has no closure; a completed or cancelled one has who and when, and only a closed task can carry a closure note
                t.HasCheckConstraint("CK_FollowUpTasks_Closure",
                    $"([Status] = '{FollowUpStatuses.Open}' AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) " +
                    $"OR ([Status] <> '{FollowUpStatuses.Open}' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL)");
                t.HasCheckConstraint("CK_FollowUpTasks_Recall", "[RecallOfTaskId] IS NULL OR [RecallOfTaskId] <> [Id]");
            });
        });

        modelBuilder.Entity<FollowUpTaskEvent>(e =>
        {
            e.ToTable("FollowUpTaskEvents");
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Detail).HasMaxLength(FollowUpRules.DetailMaxLength);
            e.HasOne<FollowUpTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TaskId, x.OccurredAtUtc });
            e.ToTable(t =>
            {
                t.HasTrigger("TR_FollowUpTaskEvents_Immutable");
                t.HasCheckConstraint("CK_FollowUpTaskEvents_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(FollowUpEventKinds.All)})");
                // only the system's reminder has no person behind it
                t.HasCheckConstraint("CK_FollowUpTaskEvents_Actor", $"[ActorUserId] IS NOT NULL OR [Kind] = '{FollowUpEventKinds.Reminded}'");
                t.HasCheckConstraint("CK_FollowUpTaskEvents_Detail", $"[Detail] IS NULL OR ({NotificationModel.NotBlank("Detail")} AND {NotificationModel.NoNul("Detail")} AND {PrescriptionModel.NoControl("Detail")})");
                t.HasCheckConstraint("CK_FollowUpTaskEvents_Time", $"[OccurredAtUtc] >= {Utc} AND [OccurredAtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<FollowUpReminderDelivery>(e =>
        {
            e.ToTable("FollowUpReminderDeliveries");
            e.Property(x => x.Channel).HasMaxLength(10);
            e.Property(x => x.Outcome).HasMaxLength(10);
            e.HasOne<FollowUpTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
            // one reminder per task, due time and channel: the dedupe record
            e.HasIndex(x => new { x.TaskId, x.DueAtUtc, x.Channel }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_FollowUpReminderDeliveries_Immutable");
                t.HasCheckConstraint("CK_FollowUpReminderDeliveries_Channel", $"[Channel] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(FollowUpChannels.All)})");
                t.HasCheckConstraint("CK_FollowUpReminderDeliveries_Outcome", $"[Outcome] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(FollowUpDeliveryOutcomes.All)})");
                t.HasCheckConstraint("CK_FollowUpReminderDeliveries_Time", $"[DueAtUtc] >= {Utc} AND [DueAtUtc] < '2200-01-01' AND [CreatedAtUtc] >= {Utc} AND [CreatedAtUtc] < '2200-01-01'");
            });
        });
    }
}
