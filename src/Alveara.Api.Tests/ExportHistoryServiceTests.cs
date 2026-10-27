using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients.Portability.Package;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, the export history list and the overview against a real SQL Server: newest first with a cursor and a limit; a person without the bulk permission sees only their own exports; the person is shown by
/// staff display name (and as unknown when there is none); a row still Started long after it began is shown as interrupted and a recent one is not, and no row is rewritten; nothing in a line names a patient; and the
/// overview lists what a package holds and leaves out, the limits, the patient and document counts, how an all-patients export would read the database, and whether the current data is within the limits.
/// </summary>
public class ExportHistoryServiceTests : MergeChartTestBase
{
    private Guid _other;

    private ExportHistoryService Service(DataExportOptions? options = null) => new(Fixture.CreateContext(), Clock, Options.Create(options ?? new DataExportOptions()));

    private async Task<Guid> RowAsync(Guid by, string result, TimeSpan ago, string kind = DataExportKind.Package, bool allPatients = false)
    {
        var id = Guid.NewGuid();
        var at = Clock.UtcNow - ago;
        var finished = result == DataExportResult.Started ? (DateTimeOffset?)null : at.AddSeconds(5);
        Guid? oneSubject = allPatients ? null : Ann;
        await using var db = Fixture.CreateContext();
        db.DataExportHistory.Add(new DataExportHistory
        {
            Id = Guid.NewGuid(), ExportId = id, Kind = kind, Scope = oneSubject is null ? DataExportScope.AllPatients : DataExportScope.OnePatient, SubjectRecordId = oneSubject, RequestedByUserId = by, RequestedAtUtc = at,
            Result = result, FinishedAtUtc = finished, PatientCount = 1, FileCount = 3, SizeBytes = 100, Sha256 = result == DataExportResult.Complete ? new string('a', 64) : null,
        });
        await db.SaveChangesAsync();
        return id;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _other = Guid.NewGuid();
        await using var db = Fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = _other, Username = $"u-{Guid.NewGuid():N}", Role = Role.Dentist, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.StaffProfiles.Add(new StaffProfile { Id = Guid.NewGuid(), UserAccountId = _other, DisplayName = "Dr. Other", CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_list_is_newest_first_and_a_cursor_and_a_limit_page_through_it()
    {
        var oldest = await RowAsync(S.Actor, DataExportResult.Complete, TimeSpan.FromHours(3));
        var middle = await RowAsync(S.Actor, DataExportResult.Complete, TimeSpan.FromHours(2));
        var newest = await RowAsync(S.Actor, DataExportResult.Complete, TimeSpan.FromHours(1));

        var all = await Service().ListAsync(S.Actor, true, 50, null, default);
        var firstPage = await Service().ListAsync(S.Actor, true, 1, null, default);
        var secondPage = await Service().ListAsync(S.Actor, true, 1, firstPage.Single().RequestedAtUtc, default);

        Assert.Equal([newest, middle, oldest], all.Select(i => i.ExportId));
        Assert.Equal(newest, firstPage.Single().ExportId);
        Assert.Equal(middle, secondPage.Single().ExportId);
    }

    [Fact]
    public async Task Without_the_bulk_permission_a_person_sees_only_their_own_exports()
    {
        var mine = await RowAsync(S.Actor, DataExportResult.Complete, TimeSpan.FromHours(2));
        var theirs = await RowAsync(_other, DataExportResult.Complete, TimeSpan.FromHours(1));

        var own = await Service().ListAsync(S.Actor, false, 50, null, default);
        var everyone = await Service().ListAsync(S.Actor, true, 50, null, default);

        Assert.Equal([mine], own.Select(i => i.ExportId));
        Assert.Equal([theirs, mine], everyone.Select(i => i.ExportId));
    }

    [Fact]
    public async Task The_person_is_a_staff_display_name_and_unknown_when_there_is_no_profile()
    {
        var unprofiled = Guid.NewGuid();
        await using (var db = Fixture.CreateContext())
        {
            db.UserAccounts.Add(new UserAccount { Id = unprofiled, Username = $"u-{Guid.NewGuid():N}", Role = Role.Dentist, CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await RowAsync(_other, DataExportResult.Complete, TimeSpan.FromHours(2));
        await RowAsync(unprofiled, DataExportResult.Complete, TimeSpan.FromHours(1));

        var items = await Service().ListAsync(S.Actor, true, 50, null, default);

        Assert.Equal("Unknown staff member", items[0].RequestedBy);                                                  // an account with no staff profile
        Assert.Equal("Dr. Other", items[1].RequestedBy);
    }

    [Fact]
    public async Task A_row_started_long_ago_is_shown_as_interrupted_a_recent_one_is_not_and_no_row_is_rewritten()
    {
        var stale = await RowAsync(S.Actor, DataExportResult.Started, TimeSpan.FromHours(5));
        var running = await RowAsync(S.Actor, DataExportResult.Started, TimeSpan.FromMinutes(2));
        var done = await RowAsync(S.Actor, DataExportResult.Complete, TimeSpan.FromHours(9));

        var items = (await Service().ListAsync(S.Actor, true, 50, null, default)).ToDictionary(i => i.ExportId);

        Assert.True(items[stale].Interrupted);
        Assert.False(items[running].Interrupted);
        Assert.False(items[done].Interrupted);
        Assert.Equal(DataExportResult.Started, items[stale].Result);                                                  // still what was recorded
        Assert.Equal(DataExportResult.Started, (await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync(h => h.ExportId == stale))).Result);
        Assert.False((await Service(new DataExportOptions { InterruptedAfterMinutes = 600 }).ListAsync(S.Actor, true, 50, null, default)).Single(i => i.ExportId == stale).Interrupted);       // the threshold is a setting
    }

    [Fact]
    public async Task A_line_names_no_patient_and_carries_the_patient_as_an_id_only()
    {
        await GiveRetiringChartAsync();
        var ann = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann));
        await RowAsync(S.Actor, DataExportResult.Complete, TimeSpan.FromHours(1), DataExportKind.ReadableRecord);
        await RowAsync(S.Actor, DataExportResult.Complete, TimeSpan.FromHours(2), allPatients: true);

        var json = JsonSerializer.Serialize(await Service().ListAsync(S.Actor, true, 50, null, default));

        Assert.DoesNotContain(ann.FirstName, json);
        Assert.DoesNotContain(ann.LastName, json);
        Assert.Contains(Ann.ToString(), json);
        Assert.Contains("\"PatientId\":null", json);                                                                  // an all-patients line has no patient
    }

    [Fact]
    public async Task The_overview_says_what_a_package_holds_and_leaves_out_the_limits_the_counts_and_how_it_would_read()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var patients = await WithDb(db => db.Patients.CountAsync(p => p.MergedAtUtc == null));
        var bytes = await WithDb(db => db.Documents.SumAsync(d => (long?)d.SizeBytes)) ?? 0;

        var overview = await Service().OverviewAsync(default);

        Assert.Equal(ExportDomainCatalog.Domains.Select(d => d.Key), overview.Domains.Select(d => d.Key));
        Assert.Equal(ExportDomainCatalog.Excluded.Select(g => g.Key), overview.Excluded.Select(g => g.Key));
        Assert.All(overview.Excluded, g => Assert.False(string.IsNullOrWhiteSpace(g.Reason)));
        Assert.Equal((5000, 2L * 1024 * 1024 * 1024, "IdOnly"), (overview.MaxPatients, overview.MaxPackageBytes, overview.DefaultFolderNaming));
        Assert.Equal((patients, bytes), (overview.PatientCount, overview.DocumentBytes));
        Assert.Equal("TableByTable", overview.AllPatientsReadMode);                                                    // snapshot isolation is off in a fresh database
        Assert.True(overview.AllPatientsWithinLimits);
        Assert.False((await Service(new DataExportOptions { MaxPatients = 1 }).OverviewAsync(default)).AllPatientsWithinLimits);
    }
}
