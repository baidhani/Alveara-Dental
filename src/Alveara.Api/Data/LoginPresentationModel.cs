using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.LoginPresentation;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-N020-C01: the sign-in presentation model and account-scoped permission grants. Closed sets and shapes are check constraints; triggers (created in the migration;
/// error codes 51276 to 51279): an added background is never deleted (51276); a background that is the current selection cannot be made unavailable (51277);
/// the stored facts of an added background (hash, storage name, type, size, dimensions, who/when added) never change (51278); the selection may only name an
/// existing, active added background (51279). The selection/availability triggers read with READCOMMITTEDLOCK, so a concurrent select and inactivate cannot both win.
/// </summary>
internal static class LoginPresentationModel
{
    private const string Utc = "'2000-01-01'";
    public static readonly DateTimeOffset SeededAt = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountPermissionGrant>(e =>
        {
            e.ToTable("AccountPermissionGrants");
            e.Property(x => x.Permission).HasMaxLength(64);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.GrantedByUserAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserAccountId, x.Permission }).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_AccountPermissionGrants_Permission",
                    $"[Permission] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(Enum.GetNames<AccountPermission>())})");
                t.HasCheckConstraint("CK_AccountPermissionGrants_Time", $"[GrantedAtUtc] >= {Utc} AND [GrantedAtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<LoginBackgroundImage>(e =>
        {
            e.ToTable("LoginBackgroundImages");
            e.Property(x => x.DisplayName).HasMaxLength(60);
            e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.ContentType).HasMaxLength(20);
            e.Property(x => x.Extension).HasMaxLength(4);
            e.Property(x => x.StorageName).HasMaxLength(64);
            e.Property(x => x.SuggestedDisplayMode).HasMaxLength(10);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.AddedByUserAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Sha256).IsUnique();
            e.HasIndex(x => x.StorageName).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_LoginBackgroundImages_NoDelete");
                t.HasTrigger("TR_LoginBackgroundImages_Guard");
                t.HasCheckConstraint("CK_LoginBackgroundImages_Type",
                    "([ContentType] = 'image/png' AND [Extension] = 'png') OR ([ContentType] = 'image/jpeg' AND [Extension] = 'jpg')");
                t.HasCheckConstraint("CK_LoginBackgroundImages_Mode", $"[SuggestedDisplayMode] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(LoginDisplayModes.All)})");
                t.HasCheckConstraint("CK_LoginBackgroundImages_Size", "[Width] BETWEEN 1 AND 2560 AND [Height] BETWEEN 1 AND 2560 AND [SizeBytes] > 0");
                t.HasCheckConstraint("CK_LoginBackgroundImages_Hash", "[Sha256] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%'");
                t.HasCheckConstraint("CK_LoginBackgroundImages_Name", "LEN(LTRIM(RTRIM([DisplayName]))) > 0");
                t.HasCheckConstraint("CK_LoginBackgroundImages_Time", $"[AddedAtUtc] >= {Utc} AND [AddedAtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<LoginPresentationSettings>(e =>
        {
            e.ToTable("LoginPresentationSettings");
            e.Property(x => x.BackgroundKind).HasMaxLength(10);
            e.Property(x => x.BundledKey).HasMaxLength(40);
            e.Property(x => x.DisplayMode).HasMaxLength(10);
            e.Property(x => x.FooterNoticeText).HasMaxLength(1000);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<LoginBackgroundImage>().WithMany().HasForeignKey(x => x.AddedImageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UpdatedByUserAccountId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_LoginPresentationSettings_Links");
                t.HasCheckConstraint("CK_LoginPresentationSettings_Singleton", $"[Id] = '{LoginPresentationSettings.SingletonId}'");
                t.HasCheckConstraint("CK_LoginPresentationSettings_Kind", $"[BackgroundKind] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(LoginBackgroundKinds.All)})");
                t.HasCheckConstraint("CK_LoginPresentationSettings_Shape",
                    $"([BackgroundKind] = '{LoginBackgroundKinds.Bundled}' AND [BundledKey] IS NOT NULL AND [AddedImageId] IS NULL) " +
                    $"OR ([BackgroundKind] = '{LoginBackgroundKinds.Added}' AND [AddedImageId] IS NOT NULL AND [BundledKey] IS NULL)");
                t.HasCheckConstraint("CK_LoginPresentationSettings_BundledKey", "[BundledKey] IS NULL OR [BundledKey] COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9-]%'");
                t.HasCheckConstraint("CK_LoginPresentationSettings_Mode", $"[DisplayMode] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(LoginDisplayModes.All)})");
                t.HasCheckConstraint("CK_LoginPresentationSettings_Notice", "[FooterNoticeText] IS NULL OR LEN(LTRIM(RTRIM([FooterNoticeText]))) > 0");
                t.HasCheckConstraint("CK_LoginPresentationSettings_Time", $"[UpdatedAtUtc] >= {Utc} AND [UpdatedAtUtc] < '2200-01-01'");
            });
            e.HasData(new LoginPresentationSettings
            {
                Id = LoginPresentationSettings.SingletonId, BackgroundKind = LoginBackgroundKinds.Bundled, BundledKey = LoginBackgroundCatalog.DefaultKey, AddedImageId = null,
                DisplayMode = LoginDisplayModes.Tile, FooterNoticeText = null, ShowFooterNotice = true, UpdatedAtUtc = SeededAt, UpdatedByUserAccountId = null,
            });
        });
    }
}
