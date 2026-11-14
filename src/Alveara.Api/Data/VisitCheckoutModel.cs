using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-N021 A2 (migration M3): the model for <see cref="VisitCheckoutOutcome"/>. The database refuses by itself what the rules refuse: only known (item, outcome) pairs; a reason where the outcome needs one and
/// never a blank one; a ledger entry exactly for the verified financial outcomes; a linked appointment only on the follow-up item; a row superseded at most once. Triggers (created in the migration):
/// an outcome is never updated or deleted (51282); a correction supersedes an earlier outcome of the SAME appointment and item (51283). Down refuses while any outcome exists (51284).
/// </summary>
internal static class VisitCheckoutModel
{
    private static string Pairs() => string.Join(" OR ", CheckoutOutcomes.ByItem.Select(kv =>
        $"([ItemCode] = '{kv.Key}' AND [OutcomeCode] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(kv.Value)}))"));

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VisitCheckoutOutcome>(e =>
        {
            e.ToTable("VisitCheckoutOutcomes");
            e.Property(x => x.ItemCode).HasMaxLength(20);
            e.Property(x => x.OutcomeCode).HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(CheckoutOutcomes.ReasonMax);
            e.Property(x => x.IdempotencyKey).HasMaxLength(CheckoutOutcomes.KeyMax);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.LinkedAppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<VisitCheckoutOutcome>().WithMany().HasForeignKey(x => x.SupersedesId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AppointmentId, x.ItemCode, x.RecordedAtUtc });
            e.HasIndex(x => x.SupersedesId).IsUnique().HasFilter("[SupersedesId] IS NOT NULL");
            e.HasIndex(x => new { x.ActorUserId, x.IdempotencyKey }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_VisitCheckoutOutcomes_Immutable");
                t.HasTrigger("TR_VisitCheckoutOutcomes_Supersedes");
                // case-sensitive: the application treats any other spelling as unknown
                t.HasCheckConstraint("CK_VisitCheckoutOutcomes_Pair", $"[ItemCode] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(CheckoutItems.All)}) AND ({Pairs()})");
                t.HasCheckConstraint("CK_VisitCheckoutOutcomes_Reason",
                    $"([Reason] IS NULL OR {NotificationModel.NotBlank("Reason")}) AND ([OutcomeCode] NOT IN ({PrescriptionModel.In(CheckoutOutcomes.NeedReason)}) OR [Reason] IS NOT NULL)");
                t.HasCheckConstraint("CK_VisitCheckoutOutcomes_Ledger",
                    $"([OutcomeCode] IN ({PrescriptionModel.In(CheckoutOutcomes.NeedLedgerEntry)}) AND [LinkedLedgerEntryNumber] IS NOT NULL AND [LinkedLedgerEntryNumber] > 0) " +
                    $"OR ([OutcomeCode] NOT IN ({PrescriptionModel.In(CheckoutOutcomes.NeedLedgerEntry)}) AND [LinkedLedgerEntryNumber] IS NULL)");
                t.HasCheckConstraint("CK_VisitCheckoutOutcomes_LinkedAppointment", $"[LinkedAppointmentId] IS NULL OR ([ItemCode] = '{CheckoutItems.FollowUp}' AND [LinkedAppointmentId] <> [AppointmentId])");
                t.HasCheckConstraint("CK_VisitCheckoutOutcomes_Supersedes", "[SupersedesId] IS NULL OR [SupersedesId] <> [Id]");
                t.HasCheckConstraint("CK_VisitCheckoutOutcomes_Key", "LEN([IdempotencyKey]) > 0");
                t.HasCheckConstraint("CK_VisitCheckoutOutcomes_Time", "[RecordedAtUtc] >= '2000-01-01' AND [RecordedAtUtc] < '2200-01-01'");
            });
        });
    }
}
