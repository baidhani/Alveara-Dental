using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-012-C01 persistence, against real SQL Server: the database itself keeps sessions, whole-tooth records, the new site measures and chart links honest, independently of the rules. It accepts the
/// full range of grades and all combinations of the optional measures; refuses grades outside 0 to 3, a furcation on a single-rooted tooth, grades on an excluded tooth, a session whose closing facts do
/// not match its status and a second draft for one patient; lets a draft's entries be edited and cleared but never those of a finalized or abandoned session; and never deletes a session or edits a
/// closed one, a whole-tooth record or a link.
/// </summary>
public class PerioSessionSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();

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

    private async Task<Guid> ExamAsync(Guid? patient = null)
    {
        await using var db = _fixture.CreateContext();
        var e = new PerioExam { Id = Guid.NewGuid(), PatientId = patient ?? _ann, IdempotencyKey = Guid.NewGuid().ToString("N"), RecordedAtUtc = At, RecordedByUserId = User, ReadingCount = 1 };
        db.PerioExams.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private async Task<Guid> SessionAsync(Guid? patient = null, Action<PerioSession>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var s = new PerioSession { Id = Guid.NewGuid(), PatientId = patient ?? _ann, Status = PerioSessionStatuses.Draft, StartedAtUtc = At, StartedByUserId = User };
        tweak?.Invoke(s);
        db.PerioSessions.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    private async Task CloseAsync(Guid session, string status)
    {
        var exam = status == PerioSessionStatuses.Finalized ? await ExamAsync() : (Guid?)null;
        await using var db = _fixture.CreateContext();
        var s = await db.PerioSessions.SingleAsync(x => x.Id == session);
        (s.Status, s.ClosedAtUtc, s.ClosedByUserId, s.ExamId) = (status, At, User, exam);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> EntryAsync(Guid session, Action<PerioSessionReading>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var r = new PerioSessionReading { Id = Guid.NewGuid(), SessionId = session, ToothKey = "16", Site = "B", ProbingDepthMm = 3, RecessionMm = 1, Bleeding = true };
        tweak?.Invoke(r);
        db.PerioSessionReadings.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    private async Task<Guid> ToothAsync(Guid session, Action<PerioSessionTooth>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var t = new PerioSessionTooth { Id = Guid.NewGuid(), SessionId = session, ToothKey = "16", Mobility = 1, Furcation = 1 };
        tweak?.Invoke(t);
        db.PerioSessionTeeth.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private async Task ToothRecordAsync(Guid exam, Action<PerioToothRecord>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var t = new PerioToothRecord { Id = Guid.NewGuid(), ExamId = exam, ToothKey = "16", Mobility = 1, Furcation = 2 };
        tweak?.Invoke(t);
        db.PerioToothRecords.Add(t);
        await db.SaveChangesAsync();
    }

    private async Task<SqlException> SqlRefusedAsync(string sql)
    {
        await using var db = _fixture.CreateContext();
        return await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
    }

    // ---------- the new site measures ----------

    [Fact]
    public async Task A_finalized_charts_reading_keeps_suppuration_and_plaque_as_true_false_or_not_assessed()
    {
        var exam = await ExamAsync();
        await using var db = _fixture.CreateContext();
        foreach (var (site, pus, plaque) in new (string, bool?, bool?)[] { ("DB", true, true), ("B", false, false), ("MB", null, null), ("DL", true, null) })
            db.PerioReadings.Add(new PerioReading { Id = Guid.NewGuid(), ExamId = exam, ToothKey = "16", Site = site, ProbingDepthMm = 3, RecessionMm = 0, Suppuration = pus, Plaque = plaque });
        await db.SaveChangesAsync();
        var stored = await _fixture.CreateContext().PerioReadings.AsNoTracking().OrderBy(r => r.Site).ToListAsync();
        Assert.Equal(new (string, bool?, bool?)[] { ("B", false, false), ("DB", true, true), ("DL", true, null), ("MB", null, null) }, stored.Select(r => (r.Site, r.Suppuration, r.Plaque)).ToArray());
    }

    // ---------- whole-tooth records ----------

    [Fact]
    public async Task A_tooth_record_accepts_every_grade_a_multirooted_tooth_and_a_not_assessed_blank()
    {
        var exam = await ExamAsync();
        for (var g = 0; g <= 3; g++) await ToothRecordAsync(exam, t => { t.ToothKey = ToothKeys.Permanent[g * 4]; t.Mobility = (byte)g; t.Furcation = null; });
        await ToothRecordAsync(exam, t => { t.ToothKey = "26"; t.Mobility = null; t.Furcation = 3; });
        await ToothRecordAsync(exam, t => { t.ToothKey = "14"; t.Mobility = 0; t.Furcation = 0; });          // a first upper premolar has more than one root
        await ToothRecordAsync(exam, t => { t.ToothKey = "37"; t.Mobility = null; t.Furcation = null; t.Excluded = true; });
        Assert.Equal(7, await _s.CountAsync(db => db.PerioToothRecords));
    }

    [Theory]
    [InlineData(4, null, "CK_PerioToothRecords_Mobility")] [InlineData(null, 4, "CK_PerioToothRecords_Furcation")] [InlineData(255, null, "CK_PerioToothRecords_Mobility")]
    public async Task A_grade_beyond_3_is_refused_by_the_database(int? mobility, int? furcation, string constraint)
    {
        var e = await RefusedAsync(async () => await ToothRecordAsync(await ExamAsync(), t => { t.Mobility = (byte?)mobility; t.Furcation = (byte?)furcation; }));
        Assert.Contains(constraint, e.Message);
    }

    [Theory]
    [InlineData("11")] [InlineData("15")] [InlineData("34")] [InlineData("43")]
    public async Task A_furcation_on_a_single_rooted_tooth_is_refused_by_the_database(string tooth)
    {
        var e = await RefusedAsync(async () => await ToothRecordAsync(await ExamAsync(), t => { t.ToothKey = tooth; t.Furcation = 1; }));
        Assert.Contains("CK_PerioToothRecords_FurcationTooth", e.Message);
    }

    [Fact]
    public async Task An_excluded_tooth_with_a_grade_a_primary_tooth_and_a_second_record_for_a_tooth_are_refused()
    {
        var exam = await ExamAsync();
        Assert.Contains("ExcludedHasNoGrades", (await RefusedAsync(async () => await ToothRecordAsync(exam, t => { t.Excluded = true; t.Mobility = 1; t.Furcation = null; }))).Message);
        Assert.Contains("CK_PerioToothRecords_ToothKey", (await RefusedAsync(async () => await ToothRecordAsync(exam, t => { t.ToothKey = "55"; t.Furcation = null; }))).Message);
        await ToothRecordAsync(exam);
        Assert.Contains("IX_PerioToothRecords_ExamId_ToothKey", (await RefusedAsync(async () => await ToothRecordAsync(exam))).Message);
    }

    [Fact]
    public async Task A_tooth_record_is_never_edited_or_deleted()
    {
        await ToothRecordAsync(await ExamAsync());
        Assert.Equal(51066, (await SqlRefusedAsync("UPDATE PerioToothRecords SET Mobility = 2")).Number);
        Assert.Equal(51066, (await SqlRefusedAsync("DELETE FROM PerioToothRecords")).Number);
    }

    // ---------- sessions ----------

    [Fact]
    public async Task A_draft_a_finalized_and_an_abandoned_session_are_each_accepted_with_the_facts_that_belong_to_them()
    {
        await SessionAsync();
        var other = await _s.PatientAsync("Bob", "Ray");
        var finalized = await SessionAsync(other);
        await CloseAsync(finalized, PerioSessionStatuses.Finalized);
        var abandoned = await SessionAsync(other);
        await CloseAsync(abandoned, PerioSessionStatuses.Abandoned);
        Assert.Equal(3, await _s.CountAsync(db => db.PerioSessions));
    }

    [Fact]
    public async Task A_second_draft_for_one_patient_is_refused_but_a_new_one_is_fine_once_the_first_is_closed()
    {
        var first = await SessionAsync();
        Assert.Contains("IX_PerioSessions_OneDraftPerPatient", (await RefusedAsync(async () => await SessionAsync())).Message);
        await SessionAsync(await _s.PatientAsync("Bob", "Ray"));                         // another patient is unaffected
        await CloseAsync(first, PerioSessionStatuses.Abandoned);
        await SessionAsync();
    }

    [Fact]
    public async Task A_session_whose_closing_facts_do_not_match_its_status_is_refused()
    {
        Assert.Contains("CK_PerioSessions_Status", (await RefusedAsync(async () => await SessionAsync(null, s => s.Status = "draft"))).Message);
        Assert.Contains("CK_PerioSessions_ClosingStamp", (await RefusedAsync(async () => await SessionAsync(null, s => s.ClosedAtUtc = At))).Message);                            // a draft that claims to be closed
        var p2 = await _s.PatientAsync("Bob", "Ray");
        Assert.Contains("CK_PerioSessions_ClosingStamp", (await RefusedAsync(async () => await SessionAsync(p2, s => s.Status = PerioSessionStatuses.Abandoned))).Message);       // closed without who and when
        var exam = await ExamAsync(p2);
        Assert.Contains("CK_PerioSessions_ExamMatchesStatus", (await RefusedAsync(async () => await SessionAsync(p2, s => { s.Status = PerioSessionStatuses.Finalized; s.ClosedAtUtc = At; s.ClosedByUserId = User; }))).Message);
        Assert.Contains("CK_PerioSessions_ExamMatchesStatus", (await RefusedAsync(async () => await SessionAsync(p2, s => s.ExamId = exam))).Message);                            // a draft that already names a chart
    }

    [Fact]
    public async Task One_chart_can_be_the_result_of_only_one_session()
    {
        var p2 = await _s.PatientAsync("Bob", "Ray");
        var first = await SessionAsync(p2);
        await CloseAsync(first, PerioSessionStatuses.Finalized);
        var exam = (await _fixture.CreateContext().PerioSessions.AsNoTracking().SingleAsync(s => s.Id == first)).ExamId!.Value;
        var e = await RefusedAsync(async () => await SessionAsync(p2, s => { s.Status = PerioSessionStatuses.Finalized; s.ClosedAtUtc = At; s.ClosedByUserId = User; s.ExamId = exam; }));
        Assert.Contains("IX_PerioSessions_ExamId", e.Message);
    }

    [Fact]
    public async Task A_session_is_never_deleted_and_a_closed_one_is_never_edited()
    {
        var draft = await SessionAsync();
        Assert.Equal(51067, (await SqlRefusedAsync("DELETE FROM PerioSessions")).Number);
        await using (var db = _fixture.CreateContext())
        {
            var s = await db.PerioSessions.SingleAsync();
            s.UpdatedAtUtc = At;                                                         // a draft may be edited
            await db.SaveChangesAsync();
        }
        await CloseAsync(draft, PerioSessionStatuses.Abandoned);
        Assert.Equal(51068, (await SqlRefusedAsync("UPDATE PerioSessions SET UpdatedByUserId = NULL")).Number);
        Assert.Equal(51067, (await SqlRefusedAsync("DELETE FROM PerioSessions")).Number);
    }

    // ---------- a draft's entries ----------

    [Fact]
    public async Task A_drafts_entries_can_be_added_changed_and_cleared()
    {
        var draft = await SessionAsync();
        var reading = await EntryAsync(draft);
        var tooth = await ToothAsync(draft);
        await using var db = _fixture.CreateContext();
        (await db.PerioSessionReadings.SingleAsync(r => r.Id == reading)).ProbingDepthMm = 6;
        (await db.PerioSessionTeeth.SingleAsync(t => t.Id == tooth)).Mobility = 2;
        await db.SaveChangesAsync();
        db.PerioSessionReadings.RemoveRange(db.PerioSessionReadings);
        db.PerioSessionTeeth.RemoveRange(db.PerioSessionTeeth);
        await db.SaveChangesAsync();
        Assert.Equal((0, 0), (await _s.CountAsync(d => d.PerioSessionReadings), await _s.CountAsync(d => d.PerioSessionTeeth)));
    }

    [Theory]
    [InlineData(PerioSessionStatuses.Finalized)] [InlineData(PerioSessionStatuses.Abandoned)]
    public async Task The_entries_of_a_finalized_or_abandoned_session_can_no_longer_be_added_changed_or_cleared(string status)
    {
        var session = await SessionAsync();
        await EntryAsync(session);
        await ToothAsync(session);
        await CloseAsync(session, status);
        Assert.Equal(51069, (await RefusedAsync(async () => await EntryAsync(session, r => r.Site = "MB"))).Number);
        Assert.Equal(51069, (await RefusedAsync(async () => await ToothAsync(session, t => t.ToothKey = "17"))).Number);
        foreach (var sql in new[] { "UPDATE PerioSessionReadings SET ProbingDepthMm = 4", "DELETE FROM PerioSessionReadings", "UPDATE PerioSessionTeeth SET Mobility = 3", "DELETE FROM PerioSessionTeeth" })
            Assert.Equal(51069, (await SqlRefusedAsync(sql)).Number);
        Assert.Equal((1, 1), (await _s.CountAsync(d => d.PerioSessionReadings), await _s.CountAsync(d => d.PerioSessionTeeth)));
    }

    [Theory]
    [InlineData("X")] [InlineData("b")] [InlineData("")]
    public async Task A_session_entry_must_name_one_of_the_six_sites_exactly(string site)
        => Assert.Contains("CK_PerioSessionReadings_Site", (await RefusedAsync(async () => await EntryAsync(await SessionAsync(), r => r.Site = site))).Message);

    [Fact]
    public async Task A_session_entry_keeps_the_same_millimetre_range_tooth_rule_and_one_reading_per_site()
    {
        var draft = await SessionAsync();
        Assert.Contains("CK_PerioSessionReadings_ProbingDepth", (await RefusedAsync(async () => await EntryAsync(draft, r => r.ProbingDepthMm = 16))).Message);
        Assert.Contains("CK_PerioSessionReadings_Recession", (await RefusedAsync(async () => await EntryAsync(draft, r => r.RecessionMm = 16))).Message);
        Assert.Contains("CK_PerioSessionReadings_ToothKey", (await RefusedAsync(async () => await EntryAsync(draft, r => r.ToothKey = "55"))).Message);
        await EntryAsync(draft);
        Assert.Contains("IX_PerioSessionReadings_SessionId_ToothKey_Site", (await RefusedAsync(async () => await EntryAsync(draft))).Message);
    }

    [Fact]
    public async Task A_session_tooth_keeps_the_grade_furcation_and_exclusion_rules_and_one_record_per_tooth()
    {
        var draft = await SessionAsync();
        Assert.Contains("CK_PerioSessionTeeth_Mobility", (await RefusedAsync(async () => await ToothAsync(draft, t => t.Mobility = 4))).Message);
        Assert.Contains("CK_PerioSessionTeeth_FurcationTooth", (await RefusedAsync(async () => await ToothAsync(draft, t => { t.ToothKey = "11"; t.Furcation = 1; }))).Message);
        Assert.Contains("ExcludedHasNoGrades", (await RefusedAsync(async () => await ToothAsync(draft, t => t.Excluded = true))).Message);
        await ToothAsync(draft);
        Assert.Contains("IX_PerioSessionTeeth_SessionId_ToothKey", (await RefusedAsync(async () => await ToothAsync(draft))).Message);
    }

    // ---------- links ----------

    private async Task LinkAsync(Guid exam, string type = "Diagnosis", string reference = "dx-1")
    {
        await using var db = _fixture.CreateContext();
        db.PerioExamLinks.Add(new PerioExamLink { Id = Guid.NewGuid(), ExamId = exam, PatientId = _ann, LinkType = type, Reference = reference, CreatedAtUtc = At, CreatedByUserId = User });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_chart_can_be_linked_once_per_type_and_reference_and_a_link_is_never_edited_or_deleted()
    {
        var exam = await ExamAsync();
        foreach (var type in PerioLinkTypes.All) await LinkAsync(exam, type);
        Assert.Contains("IX_PerioExamLinks_ExamId_LinkType_Reference", (await RefusedAsync(async () => await LinkAsync(exam))).Message);
        Assert.Equal(51070, (await SqlRefusedAsync("UPDATE PerioExamLinks SET Reference = 'x'")).Number);
        Assert.Equal(51070, (await SqlRefusedAsync("DELETE FROM PerioExamLinks")).Number);
    }

    [Theory]
    [InlineData("diagnosis", "dx-1", "CK_PerioExamLinks_LinkType")] [InlineData("Procedure", "p-1", "CK_PerioExamLinks_LinkType")] [InlineData("Diagnosis", "   ", "CK_PerioExamLinks_ReferenceNotBlank")]
    public async Task A_link_must_have_a_known_type_in_exactly_that_case_and_a_reference(string type, string reference, string constraint)
        => Assert.Contains(constraint, (await RefusedAsync(async () => await LinkAsync(await ExamAsync(), type, reference))).Message);
}
