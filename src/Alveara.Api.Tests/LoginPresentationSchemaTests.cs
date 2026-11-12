using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.LoginPresentation;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N020-C01, the database on its own (rows written straight to the tables, around any service): the seeded default; the closed sets and shapes; an added
/// background is never deleted (51276) and its stored facts never change (51278); the current selection cannot be made unavailable (51277); the selection can
/// only name an available background (51279); one grant per account and permission, and only known account permissions; and the migration rolls back and forward
/// while nothing was granted or added and refuses to roll back over either (51276).
/// </summary>
public class LoginPresentationSchemaTests : SafetyTestBase
{
    private const string BeforeThisMigration = "20261112140957_AddStaffInactivationFailedNotification";

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusalAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        return await Refusal(() => db.Database.ExecuteSqlAsync(sql));
    }

    private LoginBackgroundImage Image(Action<LoginBackgroundImage>? change = null)
    {
        var i = new LoginBackgroundImage
        {
            Id = Guid.NewGuid(), DisplayName = "Waiting room", Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ContentType = "image/png", Extension = "png",
            Width = 800, Height = 600, SizeBytes = 12345, StorageName = $"{Guid.NewGuid():N}.png", SuggestedDisplayMode = LoginDisplayModes.Fill, IsActive = true,
            AddedByUserAccountId = S.Actor, AddedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(i);
        return i;
    }

    private async Task SelectAsync(Guid imageId)
    {
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlAsync($"UPDATE [LoginPresentationSettings] SET [BackgroundKind] = 'Added', [BundledKey] = NULL, [AddedImageId] = {imageId}");
    }

    [Fact]
    public async Task The_seeded_presentation_is_the_teal_doodle_tile_with_the_default_notice_shown()
    {
        await using var db = Fixture.CreateContext();
        var s = await db.LoginPresentationSettings.SingleAsync();
        Assert.Equal((LoginBackgroundKinds.Bundled, "teal-doodle", LoginDisplayModes.Tile, (string?)null, true),
            (s.BackgroundKind, s.BundledKey, s.DisplayMode, s.FooterNoticeText, s.ShowFooterNotice));
    }

    [Fact]
    public async Task Closed_sets_and_shapes_are_enforced()
    {
        Assert.Equal(547, (await SqlRefusalAsync($"UPDATE [LoginPresentationSettings] SET [DisplayMode] = 'Centered'")).Number);
        Assert.Equal(547, (await SqlRefusalAsync($"UPDATE [LoginPresentationSettings] SET [BundledKey] = '../etc'")).Number);
        Assert.Equal(547, (await SqlRefusalAsync($"UPDATE [LoginPresentationSettings] SET [BackgroundKind] = 'Added'")).Number);           // Added without an image
        Assert.Equal(547, (await SqlRefusalAsync($"UPDATE [LoginPresentationSettings] SET [FooterNoticeText] = '   '")).Number);
        Assert.Equal(547, (await Refusal(() => AddAsync(Image(i => i.ContentType = "image/svg+xml")))).Number);
        Assert.Equal(547, (await Refusal(() => AddAsync(Image(i => i.Width = 4096)))).Number);                                        // derivatives are at most 2,560 px
        Assert.Equal(547, (await Refusal(() => AddAsync(Image(i => i.Sha256 = new string('Z', 64))))).Number);
    }

    [Fact]
    public async Task An_added_background_is_never_deleted_and_its_stored_facts_never_change()
    {
        var image = Image();
        await AddAsync(image);
        Assert.Equal(51276, (await SqlRefusalAsync($"DELETE FROM [LoginBackgroundImages] WHERE [Id] = {image.Id}")).Number);
        Assert.Equal(51278, (await SqlRefusalAsync($"UPDATE [LoginBackgroundImages] SET [StorageName] = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png' WHERE [Id] = {image.Id}")).Number);
        Assert.Equal(51278, (await SqlRefusalAsync($"UPDATE [LoginBackgroundImages] SET [Sha256] = {new string('b', 64)} WHERE [Id] = {image.Id}")).Number);
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlAsync($"UPDATE [LoginBackgroundImages] SET [DisplayName] = 'Renamed', [IsActive] = 0 WHERE [Id] = {image.Id}");   // name and availability may change
    }

    [Fact]
    public async Task The_current_selection_cannot_be_made_unavailable_and_only_an_available_background_can_be_selected()
    {
        var shown = Image();
        var other = Image(i => i.IsActive = false);
        await AddAsync(shown, other);
        await SelectAsync(shown.Id);
        Assert.Equal(51277, (await SqlRefusalAsync($"UPDATE [LoginBackgroundImages] SET [IsActive] = 0 WHERE [Id] = {shown.Id}")).Number);
        Assert.Equal(51279, (await SqlRefusalAsync($"UPDATE [LoginPresentationSettings] SET [AddedImageId] = {other.Id}")).Number);
        await using var db = Fixture.CreateContext();
        Assert.True(await db.LoginBackgroundImages.Where(i => i.Id == shown.Id).Select(i => i.IsActive).SingleAsync());
    }

    [Fact]
    public async Task One_grant_per_account_and_permission_and_only_known_account_permissions()
    {
        await AddAsync(new AccountPermissionGrant { Id = Guid.NewGuid(), UserAccountId = S.Actor, Permission = nameof(AccountPermission.ManageLoginPresentation), GrantedAtUtc = DateTimeOffset.UtcNow });
        var duplicate = await Refusal(() => AddAsync(new AccountPermissionGrant { Id = Guid.NewGuid(), UserAccountId = S.Actor, Permission = nameof(AccountPermission.ManageLoginPresentation), GrantedAtUtc = DateTimeOffset.UtcNow }));
        Assert.True(duplicate.Number is 2601 or 2627);
        Assert.Equal(547, (await Refusal(() => AddAsync(new AccountPermissionGrant { Id = Guid.NewGuid(), UserAccountId = Other, Permission = "ManageUsers", GrantedAtUtc = DateTimeOffset.UtcNow }))).Number);
    }

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_empty_and_refuses_to_roll_back_over_a_grant_or_an_added_background()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeThisMigration);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('LoginPresentationSettings','LoginBackgroundImages','AccountPermissionGrants')").SingleAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
            Assert.Equal(1, await db.LoginPresentationSettings.CountAsync());                                   // the seed is back

        await AddAsync(Image());
        var refused = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeThisMigration); });
        Assert.Equal(51276, refused.Number);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(1, await db.LoginBackgroundImages.CountAsync());                                       // history kept
    }
}
