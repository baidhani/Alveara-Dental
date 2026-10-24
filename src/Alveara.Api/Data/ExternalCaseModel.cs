using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-009-C02: the model for outside parties, referral cases, lab cases, their append-only history and their document links. As elsewhere, the database refuses by itself what the rules refuse. Triggers (created in
/// the migration; errors 51220 to 51229): a party is never deleted and its kind never changes (51220); a case is never deleted, a completed or cancelled case never changes, and its patient, creator, creation time and
/// idempotency key never change (51221); a case names a party of the matching kind, active when the case is made, and an encounter, treatment-plan item or document of its own patient (51222); a case event is never
/// updated or deleted (51223); a follow-up task names a case of its own patient (51224); the migration refuses to roll back while any of this data exists (51225).
/// </summary>
internal static class ExternalCaseModel
{
    private const string Utc = "'2000-01-01'";
    private const string Never = "'2200-01-01'";

    private static string Text(string column) => $"{NotificationModel.NotBlank(column)} AND {NotificationModel.NoNul(column)} AND {PrescriptionModel.NoControl(column)}";
    private static string Body(string column) => $"{NotificationModel.NotBlank(column)} AND {NotificationModel.NoNul(column)} AND {NotificationModel.BodyChars(column)}";

    /// <summary>The shape each status needs, the same for both kinds (the status set differs): a draft has gone nowhere; a sent or in-progress case has a sent date; a received case also a received date; a completed case both
    /// and who closed it; a cancelled case who closed it and why. Only a closed case has closure columns.</summary>
    private static string Shape() =>
        "(([Status] = 'Draft' AND [SentOn] IS NULL AND [ReceivedOn] IS NULL AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) " +
        "OR ([Status] IN ('Sent','InProgress') AND [SentOn] IS NOT NULL AND [ReceivedOn] IS NULL AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) " +
        "OR ([Status] = 'Received' AND [SentOn] IS NOT NULL AND [ReceivedOn] IS NOT NULL AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) " +
        "OR ([Status] = 'Completed' AND [SentOn] IS NOT NULL AND [ReceivedOn] IS NOT NULL AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL) " +
        "OR ([Status] = 'Cancelled' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [ClosureNote] IS NOT NULL))";

    private static string Dates() =>
        $"([SentOn] IS NULL OR ([SentOn] >= {Utc} AND [SentOn] < {Never})) AND ([ReceivedOn] IS NULL OR ([ReceivedOn] >= {Utc} AND [ReceivedOn] < {Never})) " +
        $"AND ([DueOn] IS NULL OR ([DueOn] >= {Utc} AND [DueOn] < {Never})) AND ([ReceivedOn] IS NULL OR [SentOn] IS NOT NULL) AND ([ReceivedOn] IS NULL OR [ReceivedOn] >= [SentOn]) " +
        $"AND [CreatedAtUtc] >= {Utc} AND [CreatedAtUtc] < {Never} AND [UpdatedAtUtc] >= [CreatedAtUtc] AND ([ClosedAtUtc] IS NULL OR [ClosedAtUtc] >= [CreatedAtUtc])";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExternalParty>(e =>
        {
            e.ToTable("ExternalParties");
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(ExternalCaseRules.PartyNameMaxLength);
            e.Property(x => x.ContactDetails).HasMaxLength(ExternalCaseRules.ContactMaxLength);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            // one party of a kind per name (the database's collation is case-insensitive, so "Acme Lab" and "ACME LAB" are the same)
            e.HasIndex(x => new { x.Kind, x.Name }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ExternalParties_Guard");
                t.HasCheckConstraint("CK_ExternalParties_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(ExternalPartyKinds.All)})");
                t.HasCheckConstraint("CK_ExternalParties_Name", Text("Name"));
                t.HasCheckConstraint("CK_ExternalParties_Contact", $"[ContactDetails] IS NULL OR ({Text("ContactDetails")})");
                t.HasCheckConstraint("CK_ExternalParties_Times", $"[CreatedAtUtc] >= {Utc} AND [CreatedAtUtc] < {Never} AND [UpdatedAtUtc] >= [CreatedAtUtc]");
            });
        });

        modelBuilder.Entity<ReferralCase>(e =>
        {
            e.ToTable("ReferralCases");
            e.Property(x => x.Reason).HasMaxLength(ExternalCaseRules.ReasonMaxLength);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.Notes).HasMaxLength(ExternalCaseRules.NoteMaxLength);
            e.Property(x => x.ClosureNote).HasMaxLength(ExternalCaseRules.ReasonMaxLength);
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ExternalParty>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
            // a retried or double-clicked create with the same key can only ever make one case
            e.HasIndex(x => new { x.PatientId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.HasIndex(x => new { x.Status, x.DueOn });
            e.HasIndex(x => x.PartyId);
            e.HasIndex(x => x.EncounterId).HasFilter("[EncounterId] IS NOT NULL");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ReferralCases_Guard");
                t.HasTrigger("TR_ReferralCases_Links");
                t.HasCheckConstraint("CK_ReferralCases_Status", $"[Status] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(ExternalCaseRules.ReferralStatuses)})");
                t.HasCheckConstraint("CK_ReferralCases_Shape", Shape());
                t.HasCheckConstraint("CK_ReferralCases_Dates", Dates());
                t.HasCheckConstraint("CK_ReferralCases_Reason", Text("Reason"));
                t.HasCheckConstraint("CK_ReferralCases_Notes", $"[Notes] IS NULL OR ({Body("Notes")})");
                t.HasCheckConstraint("CK_ReferralCases_ClosureNote", $"[ClosureNote] IS NULL OR ({Body("ClosureNote")})");
                t.HasCheckConstraint("CK_ReferralCases_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {PrescriptionModel.NoControl("IdempotencyKey")}");
            });
        });

        modelBuilder.Entity<LabCase>(e =>
        {
            e.ToTable("LabCases");
            e.Property(x => x.Title).HasMaxLength(ExternalCaseRules.TitleMaxLength);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Material).HasMaxLength(ExternalCaseRules.MaterialMaxLength);
            e.Property(x => x.Shade).HasMaxLength(ExternalCaseRules.ShadeMaxLength);
            e.Property(x => x.Notes).HasMaxLength(ExternalCaseRules.NoteMaxLength);
            e.Property(x => x.ClosureNote).HasMaxLength(ExternalCaseRules.ReasonMaxLength);
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ExternalParty>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TreatmentPlanItem>().WithMany().HasForeignKey(x => x.TreatmentPlanItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.HasIndex(x => new { x.Status, x.DueOn });
            e.HasIndex(x => x.PartyId);
            e.HasIndex(x => x.TreatmentPlanItemId).HasFilter("[TreatmentPlanItemId] IS NOT NULL");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_LabCases_Guard");
                t.HasTrigger("TR_LabCases_Links");
                t.HasCheckConstraint("CK_LabCases_Status", $"[Status] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(ExternalCaseRules.LabStatuses)})");
                t.HasCheckConstraint("CK_LabCases_Shape", Shape());
                t.HasCheckConstraint("CK_LabCases_Dates", Dates());
                t.HasCheckConstraint("CK_LabCases_Title", Text("Title"));
                t.HasCheckConstraint("CK_LabCases_ToothKey", $"[ToothKey] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(ToothKeys.All)})");
                t.HasCheckConstraint("CK_LabCases_Material", $"[Material] IS NULL OR ({Text("Material")})");
                t.HasCheckConstraint("CK_LabCases_Shade", $"[Shade] IS NULL OR ({Text("Shade")})");
                t.HasCheckConstraint("CK_LabCases_Notes", $"[Notes] IS NULL OR ({Body("Notes")})");
                t.HasCheckConstraint("CK_LabCases_ClosureNote", $"[ClosureNote] IS NULL OR ({Body("ClosureNote")})");
                t.HasCheckConstraint("CK_LabCases_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {PrescriptionModel.NoControl("IdempotencyKey")}");
            });
        });

        modelBuilder.Entity<ExternalCaseEvent>(e =>
        {
            e.ToTable("ExternalCaseEvents");
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.FromStatus).HasMaxLength(12);
            e.Property(x => x.ToStatus).HasMaxLength(12);
            e.Property(x => x.Note).HasMaxLength(ExternalCaseRules.ReasonMaxLength);
            e.HasOne<ReferralCase>().WithMany().HasForeignKey(x => x.ReferralCaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LabCase>().WithMany().HasForeignKey(x => x.LabCaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PatientDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ReferralCaseId, x.OccurredAtUtc }).HasFilter("[ReferralCaseId] IS NOT NULL");
            e.HasIndex(x => new { x.LabCaseId, x.OccurredAtUtc }).HasFilter("[LabCaseId] IS NOT NULL");
            e.HasIndex(x => x.DocumentId).HasFilter("[DocumentId] IS NOT NULL");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ExternalCaseEvents_Immutable");
                t.HasCheckConstraint("CK_ExternalCaseEvents_Case", "([ReferralCaseId] IS NOT NULL AND [LabCaseId] IS NULL) OR ([ReferralCaseId] IS NULL AND [LabCaseId] IS NOT NULL)");
                t.HasCheckConstraint("CK_ExternalCaseEvents_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(ExternalCaseEventKinds.All)})");
                t.HasCheckConstraint("CK_ExternalCaseEvents_Shape",
                    "([Kind] = 'StatusChanged' AND [ToStatus] IS NOT NULL AND [DocumentId] IS NULL) " +
                    "OR ([Kind] IN ('DocumentLinked','DocumentUnlinked') AND [DocumentId] IS NOT NULL AND [FromStatus] IS NULL AND [ToStatus] IS NULL) " +
                    "OR ([Kind] IN ('Created','Edited') AND [DocumentId] IS NULL AND [FromStatus] IS NULL AND ([ToStatus] IS NULL OR [Kind] = 'Created'))");
                t.HasCheckConstraint("CK_ExternalCaseEvents_Note", $"[Note] IS NULL OR ({Body("Note")})");
                t.HasCheckConstraint("CK_ExternalCaseEvents_Time", $"[OccurredAtUtc] >= {Utc} AND [OccurredAtUtc] < {Never}");
            });
        });

        modelBuilder.Entity<ExternalCaseDocument>(e =>
        {
            e.ToTable("ExternalCaseDocuments");
            e.HasOne<ReferralCase>().WithMany().HasForeignKey(x => x.ReferralCaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LabCase>().WithMany().HasForeignKey(x => x.LabCaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PatientDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.LinkedByUserId).OnDelete(DeleteBehavior.Restrict);
            // a document is linked to a case at most once; linking it twice is a quiet replay, not a second row
            e.HasIndex(x => new { x.ReferralCaseId, x.DocumentId }).IsUnique().HasFilter("[ReferralCaseId] IS NOT NULL");
            e.HasIndex(x => new { x.LabCaseId, x.DocumentId }).IsUnique().HasFilter("[LabCaseId] IS NOT NULL");
            e.HasIndex(x => x.DocumentId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ExternalCaseDocuments_Links");
                t.HasCheckConstraint("CK_ExternalCaseDocuments_Case", "([ReferralCaseId] IS NOT NULL AND [LabCaseId] IS NULL) OR ([ReferralCaseId] IS NULL AND [LabCaseId] IS NOT NULL)");
                t.HasCheckConstraint("CK_ExternalCaseDocuments_Time", $"[LinkedAtUtc] >= {Utc} AND [LinkedAtUtc] < {Never}");
            });
        });
    }
}
