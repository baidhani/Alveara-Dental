using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-N017: the model for the stored permission policy. Roles and permissions are stored as names. Constraints keep the closed sets that can be closed (the effect of an override, the kind of a history
/// row, the one state row, a version never below zero) but deliberately do NOT constrain role or permission values: persisted policy data that is wrong must be representable so the engine's own validation
/// (which never restores access on a bad row) can be proven. Triggers (created in the migration; error codes 51204 to 51206): policy history is never updated or deleted (51204); the policy version only moves
/// forward by one and its row is never deleted (51205); the migration refuses to roll back while a history row exists (51206).
/// </summary>
internal static class PermissionPolicyModel
{
    private const string Utc = "'2000-01-01'";
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    public static Guid DelegationSeedId(Role target) => Guid.Parse($"0e1e9a7e-0000-4000-8000-{(int)target:D12}");

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RolePermissionOverride>(e =>
        {
            e.ToTable("RolePermissionOverrides");
            e.Property(x => x.Role).HasMaxLength(32);
            e.Property(x => x.Permission).HasMaxLength(64);
            e.Property(x => x.Effect).HasMaxLength(8);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Role, x.Permission }).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_RolePermissionOverrides_Effect", $"[Effect] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(OverrideEffects.All)})");
                t.HasCheckConstraint("CK_RolePermissionOverrides_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                t.HasCheckConstraint("CK_RolePermissionOverrides_Time", $"[ChangedAtUtc] >= {Utc} AND [ChangedAtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<RolePermissionHistory>(e =>
        {
            e.ToTable("RolePermissionHistories");
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Role).HasMaxLength(32);
            e.Property(x => x.Permission).HasMaxLength(64);
            e.Property(x => x.TargetRole).HasMaxLength(32);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Role, x.Permission, x.PolicyVersion });
            e.HasIndex(x => x.PolicyVersion);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_RolePermissionHistories_Immutable");
                t.HasCheckConstraint("CK_RolePermissionHistories_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(PolicyChangeKinds.All)})");
                t.HasCheckConstraint("CK_RolePermissionHistories_Version", "[PolicyVersion] > 0");
                t.HasCheckConstraint("CK_RolePermissionHistories_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                t.HasCheckConstraint("CK_RolePermissionHistories_Time", $"[ChangedAtUtc] >= {Utc} AND [ChangedAtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<PermissionDelegation>(e =>
        {
            e.ToTable("PermissionDelegations");
            e.Property(x => x.DelegateRole).HasMaxLength(32);
            e.Property(x => x.Permission).HasMaxLength(64);
            e.Property(x => x.TargetRole).HasMaxLength(32);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DelegateRole, x.Permission, x.TargetRole }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_PermissionDelegations_Time", $"[ChangedAtUtc] >= {Utc} AND [ChangedAtUtc] < '2200-01-01'"));
            // the default delegation: the office manager may grant or revoke document import for the five other working roles
            e.HasData(new[] { Role.Dentist, Role.Hygienist, Role.Assistant, Role.FrontDesk, Role.Billing }.Select(target => new PermissionDelegation
            {
                Id = DelegationSeedId(target), DelegateRole = nameof(Role.OfficeManager), Permission = nameof(Permission.ImportDocuments), TargetRole = target.ToString(), ChangedAtUtc = SeededAt,
            }));
        });

        modelBuilder.Entity<PermissionPolicyState>(e =>
        {
            e.ToTable("PermissionPolicyState");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PermissionPolicyState_Guard");
                t.HasCheckConstraint("CK_PermissionPolicyState_OneRow", "[Id] = 1");
                t.HasCheckConstraint("CK_PermissionPolicyState_Version", "[Version] >= 0");
            });
            e.HasData(new PermissionPolicyState { Id = 1, Version = 0, UpdatedAtUtc = SeededAt });
        });
    }
}
