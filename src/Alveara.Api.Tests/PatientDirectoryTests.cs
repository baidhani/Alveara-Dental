using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Patients;
using Xunit;
using static Alveara.Api.Tests.PatientTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-003-C01: patient search, identity summary/detail and edit history.</summary>
public class PatientDirectoryTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private PatientTestSupport _s = null!;

    public async Task InitializeAsync() { await _fixture.InitializeAsync(); _s = new PatientTestSupport(_fixture); }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<string[]> SearchAsync(string? q, bool includeInactive = false, int take = 25)
    {
        await using var db = _fixture.CreateContext();
        return (await _s.Directory(db).SearchAsync(q, includeInactive, take, default)).Select(p => $"{p.FirstName} {p.LastName}").ToArray();
    }

    private async Task SeedAsync()
    {
        await _s.RegisterAsync("Ann", "Lee", "1985-03-09", "(555) 010-0100");
        await _s.RegisterAsync("Anna", "Leigh", "1990-05-05", "555-020-0200");
        await _s.RegisterAsync("Ben", "Smith-Jones", "1975-12-31", "+1 555 030 0300");
        await _s.RegisterAsync("Cara", "Smith", "2001-02-02", "555.040.0400");
    }

    [Fact]
    public async Task Name_words_match_the_start_of_first_or_last_names_and_every_word_must_match()
    {
        await SeedAsync();

        Assert.Equal(new[] { "Ann Lee", "Anna Leigh" }, await SearchAsync("ann"));
        Assert.Equal(new[] { "Ann Lee" }, await SearchAsync("ann lee"));
        Assert.Equal(new[] { "Ann Lee" }, await SearchAsync("LEE ann"));   // order and case do not matter
        Assert.Equal(new[] { "Cara Smith", "Ben Smith-Jones" }, await SearchAsync("smith")); // "Smith" sorts before "Smith-Jones"
        Assert.Empty(await SearchAsync("ee"));                              // prefix, not substring
        Assert.Empty(await SearchAsync("zzz"));
    }

    [Fact]
    public async Task Search_understands_a_birth_date_and_a_phone_number_in_any_formatting()
    {
        await SeedAsync();

        Assert.Equal(new[] { "Ann Lee" }, await SearchAsync("1985-03-09"));
        Assert.Equal(new[] { "Anna Leigh" }, await SearchAsync("555-020-0200"));
        Assert.Equal(new[] { "Anna Leigh" }, await SearchAsync("(555) 020 0200"));
        Assert.Equal(new[] { "Ben Smith-Jones" }, await SearchAsync("5550300300"));
        Assert.Equal(new[] { "Cara Smith" }, await SearchAsync("0400"));
    }

    [Fact]
    public async Task An_empty_search_lists_patients_in_name_order_up_to_the_requested_limit_and_never_more_than_fifty()
    {
        await SeedAsync();
        Assert.Equal(new[] { "Ann Lee", "Anna Leigh", "Ben Smith-Jones", "Cara Smith" }.Length, (await SearchAsync(null)).Length);
        Assert.Equal(new[] { "Ann Lee", "Anna Leigh" }, await SearchAsync("", take: 2));
        Assert.Single(await SearchAsync(null, take: 0)); // below 1 is raised to 1, never "unbounded"

        for (var i = 0; i < 55; i++) await _s.RegisterAsync($"Pat{i:00}", "Many", new DateOnly(1970, 1, 1).AddDays(i).ToString("yyyy-MM-dd"), $"(555) 9{i:00}-0000");
        Assert.Equal(PatientDirectory.MaxResults, (await SearchAsync(null, take: 10_000)).Length);
    }

    [Fact]
    public async Task Inactive_patients_are_hidden_from_search_unless_asked_for_and_are_never_lost()
    {
        var ann = await _s.RegisterAsync("Ann", "Lee", "1985-03-09");
        await _s.RegisterAsync("Ben", "Lee", "1980-01-01", "(555) 010-0200");
        await using (var db = _fixture.CreateContext()) await _s.Edits(db).SetActiveAsync(ann.Id, false, Version(ann), _s.Actor, default);

        Assert.Equal(new[] { "Ben Lee" }, await SearchAsync("lee"));
        Assert.Equal(new[] { "Ann Lee", "Ben Lee" }, await SearchAsync("lee", includeInactive: true));
        await using var verify = _fixture.CreateContext();
        var summary = (await _s.Directory(verify).SearchAsync("ann", true, 5, default)).Single();
        Assert.False(summary.IsActive);
    }

    [Fact]
    public async Task A_search_string_is_data_not_a_query_so_wildcards_and_injection_text_match_nothing_unusual()
    {
        await SeedAsync();
        Assert.Empty(await SearchAsync("%"));
        Assert.Empty(await SearchAsync("'; DROP TABLE Patients;--"));
        Assert.Equal(4, (await SearchAsync(null)).Length);
    }

    [Fact]
    public async Task Detail_carries_the_identity_summary_with_age_on_the_practice_calendar_date()
    {
        var today = DateOnly.FromDateTime(PatientTestSupport.Clock.ToPracticeLocal(PatientTestSupport.Clock.UtcNow).DateTime);
        var birthday = today.AddYears(-30);
        var tomorrowBirthday = today.AddYears(-30).AddDays(1); // one day before turning 30, so still 29
        var a = await _s.RegisterAsync("Thirty", "Today", birthday.ToString("yyyy-MM-dd"), "(555) 010-0001");
        var b = await _s.RegisterAsync("Almost", "Thirty", tomorrowBirthday.ToString("yyyy-MM-dd"), "(555) 010-0002");

        await using var db = _fixture.CreateContext();
        var dir = _s.Directory(db);
        Assert.Equal(30, (await dir.GetDetailAsync(a.Id, default))!.Age);
        Assert.Equal(29, (await dir.GetDetailAsync(b.Id, default))!.Age);
        Assert.Null(await dir.GetDetailAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task History_lists_edits_newest_first_with_who_and_when_and_is_null_for_an_unknown_patient()
    {
        var p = await _s.RegisterAsync("Ann", "Lee");
        await using (var db = _fixture.CreateContext())
        {
            var first = await _s.Edits(db).UpdateAsync(p.Id, Fields(p) with { City = "Dallas" }, Version(p), _s.Actor, default);
            await using var db2 = _fixture.CreateContext();
            await _s.Edits(db2).UpdateAsync(p.Id, Fields(first) with { City = "Waco" }, Version(first), _s.Actor, default);
        }

        await using var read = _fixture.CreateContext();
        var history = (await _s.Directory(read).GetHistoryAsync(p.Id, default))!;
        Assert.Equal(new[] { "Waco", "Dallas" }, history.Select(h => h.NewValue).ToArray());
        Assert.Equal("Dallas", history[0].OldValue);
        Assert.All(history, h => Assert.Equal(_s.Actor, h.ChangedByUserId));
        Assert.All(history, h => Assert.True(DateTimeOffset.TryParse(h.ChangedAtUtc, out _)));
        Assert.Null(await _s.Directory(read).GetHistoryAsync(Guid.NewGuid(), default));
    }
}
