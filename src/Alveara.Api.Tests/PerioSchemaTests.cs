using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-012 persistence, against real SQL Server: the database itself keeps periodontal charts honest, independently of <see cref="PerioRules"/>. It accepts every permanent tooth, all
/// six sites and the 0 and 15 mm edges; refuses a primary tooth, a tooth that is not an FDI key, a site that is not one of the six (case matters), depth or recession of -1 or 16 mm,
/// and a chart with no readings; allows one reading per tooth and site and one chart per idempotency key; and never edits or deletes a chart or a reading.
/// </summary>
public class PerioSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    private async Task<Guid> ExamAsync(Guid? patient = null, string? key = null, int count = 1)
    {
        await using var db = _fixture.CreateContext();
        var exam = new PerioExam { Id = Guid.NewGuid(), PatientId = patient ?? _ann, IdempotencyKey = key ?? Guid.NewGuid().ToString("N"), RecordedAtUtc = At, RecordedByUserId = Guid.NewGuid(), ReadingCount = count };
        db.PerioExams.Add(exam);
        await db.SaveChangesAsync();
        return exam.Id;
    }

    private async Task ReadingAsync(Guid exam, Action<PerioReading>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var r = new PerioReading { Id = Guid.NewGuid(), ExamId = exam, ToothKey = "16", Site = "B", ProbingDepthMm = 3, RecessionMm = 1, Bleeding = true };
        tweak?.Invoke(r);
        db.PerioReadings.Add(r);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_database_accepts_all_32_permanent_teeth_all_six_sites_and_both_edges_of_the_range()
    {
        var exam = await ExamAsync(count: 192);
        foreach (var tooth in ToothKeys.Permanent)
            foreach (var site in PerioRules.Sites)
                await ReadingAsync(exam, r => { r.ToothKey = tooth; r.Site = site; });
        await ReadingAsync(await ExamAsync(), r => { r.ProbingDepthMm = 0; r.RecessionMm = 0; });
        await ReadingAsync(await ExamAsync(), r => { r.ProbingDepthMm = 15; r.RecessionMm = 15; });
        Assert.Equal(192 + 2, await _s.CountAsync(db => db.PerioReadings));
    }

    [Theory]
    [InlineData("55")] [InlineData("19")] [InlineData("1")] [InlineData("")] [InlineData("A1")]
    public async Task A_primary_tooth_or_anything_that_is_not_a_permanent_key_is_refused(string tooth)
    {
        var e = await RefusedAsync(async () => await ReadingAsync(await ExamAsync(), r => r.ToothKey = tooth));
        Assert.Contains("CK_PerioReadings_ToothKey", e.Message);
    }

    [Theory]
    [InlineData("X")] [InlineData("b")] [InlineData("db")] [InlineData("")]
    public async Task A_site_must_be_one_of_the_six_in_exactly_that_case(string site)
    {
        var e = await RefusedAsync(async () => await ReadingAsync(await ExamAsync(), r => r.Site = site));
        Assert.Contains("CK_PerioReadings_Site", e.Message);
    }

    [Fact]
    public async Task A_site_longer_than_two_characters_does_not_fit_the_column_at_all()
        => await RefusedAsync(async () => await ReadingAsync(await ExamAsync(), r => r.Site = "DBB"));

    [Theory]
    [InlineData(16, 0, "CK_PerioReadings_ProbingDepth")] [InlineData(255, 0, "CK_PerioReadings_ProbingDepth")]
    [InlineData(0, 16, "CK_PerioReadings_Recession")] [InlineData(0, 255, "CK_PerioReadings_Recession")]
    public async Task Depth_or_recession_beyond_15_mm_is_refused_by_the_database(int depth, int recession, string constraint)
    {
        var e = await RefusedAsync(async () => await ReadingAsync(await ExamAsync(), r => { r.ProbingDepthMm = (byte)depth; r.RecessionMm = (byte)recession; }));
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task A_chart_must_claim_between_1_and_192_readings_and_a_blank_key_is_refused()
    {
        Assert.Contains("CK_PerioExams_ReadingCount", (await RefusedAsync(async () => await ExamAsync(count: 0))).Message);
        Assert.Contains("CK_PerioExams_ReadingCount", (await RefusedAsync(async () => await ExamAsync(count: 193))).Message);
        Assert.Contains("CK_PerioExams_Key", (await RefusedAsync(async () => await ExamAsync(key: "  "))).Message);
    }

    [Fact]
    public async Task A_tooth_and_site_can_be_read_once_per_chart_but_again_in_a_later_chart()
    {
        var first = await ExamAsync();
        await ReadingAsync(first);
        var e = await RefusedAsync(async () => await ReadingAsync(first));
        Assert.Contains("IX_PerioReadings_ExamId_ToothKey_Site", e.Message);
        await ReadingAsync(await ExamAsync());   // the next visit measures the same site again
    }

    [Fact]
    public async Task One_idempotency_key_makes_one_chart_per_patient_and_a_different_patient_can_reuse_it()
    {
        await ExamAsync(key: "visit-1");
        var e = await RefusedAsync(async () => await ExamAsync(key: "visit-1"));
        Assert.Contains("IX_PerioExams_PatientId_IdempotencyKey", e.Message);
        await ExamAsync(patient: await _s.PatientAsync("Bob", "Ray"), key: "visit-1");
    }

    [Fact]
    public async Task A_reading_cannot_name_a_chart_that_does_not_exist()
        => await RefusedAsync(async () => await ReadingAsync(Guid.NewGuid()));

    [Fact]
    public async Task A_chart_and_its_readings_are_never_edited_or_deleted()
    {
        var exam = await ExamAsync();
        await ReadingAsync(exam);
        await using var db = _fixture.CreateContext();
        foreach (var (sql, code) in new[]
        {
            ("UPDATE PerioExams SET ReadingCount = 2", 51064), ("DELETE FROM PerioExams", 51064),
            ("UPDATE PerioReadings SET ProbingDepthMm = 4", 51065), ("DELETE FROM PerioReadings", 51065),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(code, ex.Number);
        }
        Assert.Equal((1, 1), (await _s.CountAsync(d => d.PerioExams), await _s.CountAsync(d => d.PerioReadings)));
    }
}
