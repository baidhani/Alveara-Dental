using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;
using Alveara.Api.Architecture.LoginPresentation;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N020-C01 R02 (review items C01-2 and C01-3): two competing application operations, released together by a barrier, each on its own database context and
/// service instance, against the real database. (1) Choosing an added image races making that same image unavailable: the persisted state never has the
/// selection on an unavailable image, exactly one operation is refused with its defined conflict, and the audit history names only what really happened.
/// (2) Two uploads at 29 available images, and an upload against a reactivation at 29, never produce more than 30, the loser gets <c>background_catalog_full</c>,
/// and the refused upload leaves no derivative file behind. The database triggers stay in place as the independent backstop (LoginPresentationSchemaTests).
/// </summary>
public class LoginBackgroundCatalogConcurrencyTests : SafetyTestBase
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"alv-c01r02-{Guid.NewGuid():N}");

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LoginPresentationService Service(AlveraDbContext db) =>
        new(db, new LoginBackgroundStore(_root), new ConfigurationBuilder().Build(), NullLogger<LoginPresentationService>.Instance);

    private async Task<Exception?[]> RunTogetherAsync(params Func<LoginPresentationService, Task>[] work)
    {
        using var barrier = new Barrier(work.Length);
        var tasks = work.Select(w => Task.Run(async () =>
        {
            await using var db = Fixture.CreateContext();
            var service = Service(db);
            barrier.SignalAndWait(TimeSpan.FromSeconds(20));
            try { await w(service); return (Exception?)null; }
            catch (Exception ex) { return ex; }
        })).ToArray();
        return await Task.WhenAll(tasks);
    }

    private static string Code(Exception? e) => Assert.IsType<LoginPresentationException>(e).Code;

    private async Task SeedActiveAsync(int count, bool oneInactiveExtra = false)
    {
        await using var db = Fixture.CreateContext();
        for (var i = 0; i < count + (oneInactiveExtra ? 1 : 0); i++)
            db.LoginBackgroundImages.Add(Row(i, active: i < count));
        await db.SaveChangesAsync();
    }

    private LoginBackgroundImage Row(int i, bool active) => new()
    {
        Id = Guid.NewGuid(), DisplayName = $"Seed {i}", Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ContentType = "image/png", Extension = "png",
        Width = 800, Height = 600, SizeBytes = 1000, StorageName = $"{Guid.NewGuid():N}.png", SuggestedDisplayMode = LoginDisplayModes.Fill, IsActive = active,
        AddedByUserAccountId = S.Actor, AddedAtUtc = DateTimeOffset.UtcNow,
    };

    private static byte[] Image(byte shade) => LoginTestImages.Png(256, 256, new SKColor(shade, 60, 120));

    private async Task<int> ActiveCountAsync()
    {
        await using var db = Fixture.CreateContext();
        return await db.LoginBackgroundImages.CountAsync(i => i.IsActive);
    }

    private int FilesInStore() => Directory.Exists(_root) ? Directory.GetFiles(_root).Length : 0;

    private async Task<int> AuditCountAsync(string eventType)
    {
        await using var db = Fixture.CreateContext();
        return await db.AuditLogEntries.CountAsync(a => a.EventType == eventType);
    }

    [Fact]
    public async Task Choosing_an_added_image_races_making_it_unavailable_and_the_selection_never_ends_on_an_unavailable_image()
    {
        Guid imageId;
        await using (var db = Fixture.CreateContext())
        {
            var added = await Service(db).AddImageAsync(Image(30), "a.png", "Race image", null, true, S.Actor, CancellationToken.None);
            imageId = added.Id;
        }

        for (var round = 0; round < 6; round++)
        {
            string settingsVersion, imageVersion;
            await using (var db = Fixture.CreateContext())
            {
                settingsVersion = Convert.ToBase64String((await db.LoginPresentationSettings.AsNoTracking().SingleAsync()).RowVersion);
                imageVersion = Convert.ToBase64String((await db.LoginBackgroundImages.AsNoTracking().SingleAsync(i => i.Id == imageId)).RowVersion);
            }
            var selectedBefore = await AuditCountAsync(LoginPresentationAuditEvents.BackgroundSelected);
            var inactivatedBefore = await AuditCountAsync(LoginPresentationAuditEvents.BackgroundInactivated);

            var results = await RunTogetherAsync(
                s => s.SaveAsync(new SaveLoginPresentation(LoginBackgroundKinds.Added, null, imageId, LoginDisplayModes.Fill, null, true, true, settingsVersion), S.Actor, CancellationToken.None),
                s => s.SetImageActiveAsync(imageId, false, imageVersion, S.Actor, CancellationToken.None));

            var selectWon = results[0] is null;
            var inactivateWon = results[1] is null;
            Assert.True(selectWon ^ inactivateWon, $"round {round}: exactly one operation must succeed ({results[0]?.Message} / {results[1]?.Message})");
            Assert.Equal(selectWon ? "background_in_use" : "background_unavailable", selectWon ? Code(results[1]) : Code(results[0]));

            await using (var db = Fixture.CreateContext())
            {
                var s = await db.LoginPresentationSettings.AsNoTracking().SingleAsync();
                var image = await db.LoginBackgroundImages.AsNoTracking().SingleAsync(i => i.Id == imageId);
                Assert.False(s.AddedImageId == imageId && !image.IsActive, $"round {round}: the selection points at an unavailable image");
                Assert.Equal(selectWon, s.AddedImageId == imageId);
                Assert.Equal(!selectWon, !image.IsActive);
            }
            Assert.Equal(selectedBefore + (selectWon ? 1 : 0), await AuditCountAsync(LoginPresentationAuditEvents.BackgroundSelected));          // history names only what happened
            Assert.Equal(inactivatedBefore + (inactivateWon ? 1 : 0), await AuditCountAsync(LoginPresentationAuditEvents.BackgroundInactivated));

            await using (var db = Fixture.CreateContext())                                                                                  // reset for the next round
            {
                await db.Database.ExecuteSqlAsync($"UPDATE [LoginPresentationSettings] SET [BackgroundKind] = 'Bundled', [BundledKey] = 'teal-doodle', [AddedImageId] = NULL");
                await db.Database.ExecuteSqlAsync($"UPDATE [LoginBackgroundImages] SET [IsActive] = 1 WHERE [Id] = {imageId}");
            }
        }
    }

    [Fact]
    public async Task Two_uploads_at_29_available_images_produce_exactly_one_more_and_leave_no_orphan_file()
    {
        await SeedActiveAsync(29);
        var results = await RunTogetherAsync(
            s => s.AddImageAsync(Image(20), "a.png", "First", null, true, S.Actor, CancellationToken.None),
            s => s.AddImageAsync(Image(200), "b.png", "Second", null, true, S.Actor, CancellationToken.None));

        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal("background_catalog_full", Code(results.Single(r => r is not null)));
        Assert.Equal(30, await ActiveCountAsync());
        Assert.Equal(1, FilesInStore());                                                                                                    // the refused upload stored nothing
        Assert.Equal(1, await AuditCountAsync(LoginPresentationAuditEvents.BackgroundAdded));
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, FilesInStore());
        Assert.True(File.Exists(Path.Combine(_root, (await db.LoginBackgroundImages.AsNoTracking().SingleAsync(i => i.DisplayName == "First" || i.DisplayName == "Second")).StorageName)));
    }

    [Fact]
    public async Task An_upload_races_a_reactivation_at_29_available_images_and_the_total_never_exceeds_30()
    {
        await SeedActiveAsync(29, oneInactiveExtra: true);
        string version;
        Guid inactiveId;
        await using (var db = Fixture.CreateContext())
        {
            var inactive = await db.LoginBackgroundImages.AsNoTracking().SingleAsync(i => !i.IsActive);
            inactiveId = inactive.Id; version = Convert.ToBase64String(inactive.RowVersion);
        }

        var results = await RunTogetherAsync(
            s => s.AddImageAsync(Image(90), "c.png", "Upload", null, true, S.Actor, CancellationToken.None),
            s => s.SetImageActiveAsync(inactiveId, true, version, S.Actor, CancellationToken.None));

        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal("background_catalog_full", Code(results.Single(r => r is not null)));
        Assert.Equal(30, await ActiveCountAsync());
        var uploadWon = results[0] is null;
        Assert.Equal(uploadWon ? 1 : 0, FilesInStore());                                                                                    // a refused upload leaves no file
        Assert.Equal(uploadWon ? 1 : 0, await AuditCountAsync(LoginPresentationAuditEvents.BackgroundAdded));
        Assert.Equal(uploadWon ? 0 : 1, await AuditCountAsync(LoginPresentationAuditEvents.BackgroundReactivated));
    }
}
