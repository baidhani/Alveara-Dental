using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-012-C01 persistence, against real SQL Server: a database that already holds STORY-012 charts upgrades with every chart and reading kept (the new suppuration and plaque measures read as not
/// assessed, there are no whole-tooth records or links yet, and the session tables exist and are empty), and rolling back removes only what this migration added, then forward again loses nothing.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class PerioMigrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    public PerioMigrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private const string Before = "20261005051102_AddPeriodontalCharting";

    private async Task<bool> TableExistsAsync(string name)
    {
        await using var db = _fixture.CreateContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = {name}").AnyAsync(x => x > 0);
    }

    [Fact]
    public async Task Upgrading_a_database_that_holds_charts_keeps_every_chart_and_reading_and_rolling_back_then_forward_loses_nothing()
    {
        var s = new SchedulingTestSupport(_fixture);
        await s.ArrangeAsync();
        var patient = await s.PatientAsync();

        await using (var db = _fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before);     // the schema as STORY-012 left it
        Assert.False(await TableExistsAsync("PerioSessions"));
        Assert.False(await TableExistsAsync("PerioToothRecords"));
        Assert.False(await TableExistsAsync("PerioExamLinks"));

        var (exam, user, at) = (Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        await using (var db = _fixture.CreateContext())
        {
            Assert.Equal(Before, (await db.Database.GetAppliedMigrationsAsync()).Last());
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PerioExams (Id, PatientId, IdempotencyKey, RecordedAtUtc, RecordedByUserId, ReadingCount) VALUES ({exam}, {patient}, 'old-chart', {at}, {user}, 2)");
            foreach (var (tooth, site, depth, recession, bleeding) in new[] { ("16", "B", 5, 2, 1), ("26", "DL", 3, 0, 0) })
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PerioReadings (Id, ExamId, ToothKey, Site, ProbingDepthMm, RecessionMm, Bleeding) VALUES ({Guid.NewGuid()}, {exam}, {tooth}, {site}, {depth}, {recession}, {bleeding})");
        }

        await using (var db = _fixture.CreateContext()) await db.Database.MigrateAsync();                                          // the upgrade
        await AssertKeptAsync(exam);
        Assert.True(await TableExistsAsync("PerioSessions"));
        await using (var db = _fixture.CreateContext())
        {
            Assert.Equal((0, 0, 0, 0), (await db.PerioSessions.CountAsync(), await db.PerioSessionReadings.CountAsync(), await db.PerioToothRecords.CountAsync(), await db.PerioExamLinks.CountAsync()));
            var rows = await db.PerioReadings.AsNoTracking().OrderBy(r => r.ToothKey).ToListAsync();
            Assert.All(rows, r => Assert.Equal((null, null), (r.Suppuration, r.Plaque)));                                          // not assessed, which is not the same as none
        }

        await using (var db = _fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before);   // roll back
        Assert.False(await TableExistsAsync("PerioSessions"));
        await using (var db = _fixture.CreateContext())
        {
            Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM PerioReadings").SingleAsync());       // only what the migration added is gone
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('PerioReadings') AND name IN ('Suppuration', 'Plaque')").SingleAsync());
        }
        await using (var db = _fixture.CreateContext()) await db.Database.MigrateAsync();                                          // and forward again
        await AssertKeptAsync(exam);
    }

    private async Task AssertKeptAsync(Guid exam)
    {
        await using var db = _fixture.CreateContext();
        var stored = await db.PerioExams.AsNoTracking().SingleAsync(e => e.Id == exam);
        Assert.Equal(("old-chart", 2), (stored.IdempotencyKey, stored.ReadingCount));
        var rows = await db.PerioReadings.AsNoTracking().Where(r => r.ExamId == exam).OrderBy(r => r.ToothKey).ToListAsync();
        Assert.Equal(new[] { ("16", "B", 5, 2, true), ("26", "DL", 3, 0, false) }, rows.Select(r => (r.ToothKey, r.Site, (int)r.ProbingDepthMm, (int)r.RecessionMm, r.Bleeding)));
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM PerioReadings WHERE ExamId = {exam}").SingleAsync());
    }
}
