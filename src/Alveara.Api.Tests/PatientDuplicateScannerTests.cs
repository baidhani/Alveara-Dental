using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-017, the scan for duplicates among existing patients against a real SQL Server: a pair is suggested only when they share a birth date AND one of the registration rules applies (same last name; same first name and a last
/// name one or two typos away; the same phone of at least seven digits; the same email in any letter case); a shared surname, phone or email with different birth dates is a normal family and is never suggested; each pair is
/// reported once, strongest first, with its reasons in words and a suggested survivor (the active record, else the older); the list is capped and says when it was cut; the scan changes nothing and gives the same answer twice;
/// and its rules cannot drift from the registration check's (a parity test).
/// </summary>
public class PatientDuplicateScannerTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private static readonly DateOnly Dob = new(1985, 3, 9);
    private int _n;

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Patient> AddAsync(string first, string last, DateOnly? dob = null, string phone = "(555) 010-0100", string? email = null, bool active = true, DateTimeOffset? created = null, Guid? household = null)
    {
        var d = dob ?? Dob;
        var p = new Patient
        {
            Id = Guid.NewGuid(), FirstName = first, LastName = last, DateOfBirth = d, Phone = phone, Email = email, AddressLine1 = "1 Main St", City = "Austin", State = "TX", PostalCode = "78701",
            DuplicateKey = Patient.BuildDuplicateKey(first, last, d), IsActive = active, CreatedAtUtc = created ?? DateTimeOffset.UtcNow.AddMinutes(-(1000 - ++_n)), HouseholdId = household,
        };
        await using var db = _fixture.CreateContext();
        db.Patients.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private async Task<DuplicateScanResult> ScanAsync(int max = PatientDuplicateScanner.MaxPairs)
    {
        await using var db = _fixture.CreateContext();
        return await new PatientDuplicateScanner(db).ScanAsync(default, max);
    }

    private static string Phone(int n) => $"(555) 02{n:00}-{n:0000}";

    [Fact]
    public async Task Two_patients_with_one_birth_date_and_one_last_name_are_suggested_with_the_reason_in_words()
    {
        var a = await AddAsync("Ann", "Lee", phone: Phone(1));
        var b = await AddAsync("Anne", "Lee", phone: Phone(2));
        var result = await ScanAsync();
        var pair = Assert.Single(result.Pairs);
        Assert.Equal(new[] { PatientMatchRules.SameLastNameReason }, pair.Reasons);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), new[] { pair.First.Id, pair.Second.Id }.Order());
        Assert.Equal((40, false, 2), (pair.Score, result.Truncated, result.PatientsConsidered));
        Assert.Equal("1985-03-09", pair.First.DateOfBirth);
    }

    [Theory]
    [InlineData("Ann", "Lee", "Ann", "Lea", true)]          // one typo in the last name
    [InlineData("Ann", "Lee", "Ann", "Lii", true)]          // two typos
    [InlineData("Ann", "Lee", "Ann", "Lxxx", false)]        // three or more
    [InlineData("Ann", "Lee", "Bea", "Lea", false)]         // a similar last name needs the same first name
    public async Task A_similar_last_name_counts_only_with_the_same_first_name_and_at_most_two_typos(string f1, string l1, string f2, string l2, bool suggested)
    {
        await AddAsync(f1, l1, phone: Phone(1));
        await AddAsync(f2, l2, phone: Phone(2));
        Assert.Equal(suggested, (await ScanAsync()).Pairs.Count == 1);
    }

    [Fact]
    public async Task The_same_phone_of_seven_digits_or_more_or_the_same_email_in_any_case_is_suggested_and_a_short_number_is_not()
    {
        await AddAsync("Ann", "Lee", phone: "(555) 010-0100", email: "ann@example.test");
        await AddAsync("Bea", "Moss", phone: "555.010.0100");                                          // same digits, different formatting
        await AddAsync("Cy", "Park", phone: Phone(5), email: "ANN@Example.Test");                      // same email, other letters
        await AddAsync("Di", "Quinn", phone: "12-34", email: null);
        await AddAsync("Ed", "Rowe", phone: "12-34", email: null);                                     // a short shared number is not enough
        var pairs = (await ScanAsync()).Pairs;
        Assert.Equal(2, pairs.Count);
        Assert.Contains(pairs, p => p.Reasons.SequenceEqual(new[] { PatientMatchRules.SamePhoneReason }));
        Assert.Contains(pairs, p => p.Reasons.SequenceEqual(new[] { PatientMatchRules.SameEmailReason }));
        Assert.DoesNotContain(pairs, p => p.First.LastName is "Quinn" or "Rowe");
    }

    [Fact]
    public async Task A_shared_surname_phone_or_email_with_different_birth_dates_is_a_family_and_never_suggested()
    {
        await AddAsync("Ann", "Lee", Dob, "(555) 010-0100", "family@example.test");
        await AddAsync("Ann", "Lee", Dob.AddYears(-30), "(555) 010-0100", "family@example.test");     // the same name, phone and email, a parent
        await AddAsync("Bo", "Lee", Dob.AddDays(1), "(555) 010-0100", "family@example.test");
        var result = await ScanAsync();
        Assert.Empty(result.Pairs);
        Assert.Equal(0, result.PatientsConsidered);                                                      // nobody shares a birth date, so nobody is even compared
    }

    [Fact]
    public async Task Unrelated_patients_and_an_empty_practice_give_no_suggestion()
    {
        Assert.Empty((await ScanAsync()).Pairs);
        await AddAsync("Ann", "Lee", phone: Phone(1)); await AddAsync("Bo", "Kim", phone: Phone(2)); await AddAsync("Cy", "Park", phone: Phone(3));
        Assert.Empty((await ScanAsync()).Pairs);
    }

    [Fact]
    public async Task Three_possible_duplicates_make_three_pairs_each_reported_once_and_the_strongest_come_first()
    {
        await AddAsync("Ann", "Lee", phone: "(555) 010-0100", email: "ann@example.test");
        await AddAsync("Anne", "Lee", phone: "(555) 010-0100", email: "ann@example.test");           // same last name + phone + email with the first: 100
        await AddAsync("Bea", "Lee", phone: Phone(9));                                                  // same last name only: 40
        var pairs = (await ScanAsync()).Pairs;
        Assert.Equal(3, pairs.Count);
        Assert.Equal(new[] { 100, 40, 40 }, pairs.Select(p => p.Score));
        Assert.Equal(3, pairs.Select(p => string.Join("|", new[] { p.First.Id, p.Second.Id }.Order())).Distinct().Count());       // no pair twice, in either order
    }

    [Fact]
    public async Task The_suggested_survivor_is_the_active_record_else_the_older_one_with_the_reason()
    {
        var older = await AddAsync("Ann", "Lee", phone: Phone(1), created: DateTimeOffset.UtcNow.AddDays(-30));
        var newer = await AddAsync("Anne", "Lee", phone: Phone(2), created: DateTimeOffset.UtcNow.AddDays(-1));
        var pair = Assert.Single((await ScanAsync()).Pairs);
        Assert.Equal((older.Id, "The older record, which usually holds more history"), (pair.SuggestedSurvivorId, pair.SuggestionReason));

        await using (var db = _fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [Patients] SET [IsActive] = 0 WHERE [Id] = {older.Id}");
        var inactive = Assert.Single((await ScanAsync()).Pairs);
        Assert.Equal((newer.Id, "The other record is inactive"), (inactive.SuggestedSurvivorId, inactive.SuggestionReason));
    }

    [Fact]
    public async Task Two_members_of_one_household_are_marked_so_twins_are_not_taken_for_one_person()
    {
        Guid household;
        await using (var db = _fixture.CreateContext()) { var h = new Household { Id = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow }; db.Households.Add(h); await db.SaveChangesAsync(); household = h.Id; }
        await AddAsync("Tia", "Lee", phone: Phone(1), household: household);
        await AddAsync("Tom", "Lee", phone: Phone(2), household: household);
        await AddAsync("Cy", "Park", phone: Phone(3)); await AddAsync("Cyd", "Park", phone: Phone(4));
        var pairs = (await ScanAsync()).Pairs;
        Assert.Equal(2, pairs.Count);
        Assert.Single(pairs, p => p.SameHousehold);
        Assert.Single(pairs, p => !p.SameHousehold);
    }

    [Fact]
    public async Task The_list_is_capped_strongest_first_and_says_when_it_was_cut()
    {
        for (var i = 0; i < 4; i++) await AddAsync($"Pat{(char)('a' + i)}", "Smith", phone: Phone(10 + i));        // 6 pairs, all 40
        await AddAsync("Zed", "Zorn", phone: "(555) 010-9999", email: "z@example.test"); await AddAsync("Zedd", "Zorn", phone: "(555) 010-9999", email: "z@example.test");   // 100
        var cut = await ScanAsync(3);
        Assert.Equal((3, true), (cut.Pairs.Count, cut.Truncated));
        Assert.Equal(100, cut.Pairs[0].Score);                                                           // the strongest survives the cut
        var full = await ScanAsync();
        Assert.Equal((7, false), (full.Pairs.Count, full.Truncated));
    }

    [Fact]
    public async Task A_list_that_exactly_fits_the_limit_is_not_called_cut()
    {
        await AddAsync("Ann", "Lee", phone: Phone(1)); await AddAsync("Anne", "Lee", phone: Phone(2)); await AddAsync("Bea", "Lee", phone: Phone(3));      // 3 pairs
        Assert.Equal((3, false), ((await ScanAsync(3)).Pairs.Count, (await ScanAsync(3)).Truncated));
        Assert.Equal((2, true), ((await ScanAsync(2)).Pairs.Count, (await ScanAsync(2)).Truncated));
    }

    [Fact]
    public async Task Patients_of_different_birth_dates_are_never_compared_even_when_each_birth_date_is_shared_with_someone_else()
    {
        await AddAsync("Ann", "Lee", Dob, Phone(1)); await AddAsync("Bo", "Kim", Dob, Phone(2));                                            // one birth date, two unrelated people
        await AddAsync("Ann", "Lee", Dob.AddYears(-30), Phone(3)); await AddAsync("Cy", "Park", Dob.AddYears(-30), Phone(4));              // another, two unrelated people; the two Lee records share a name across the dates
        var result = await ScanAsync();
        Assert.Empty(result.Pairs);
        Assert.Equal(4, result.PatientsConsidered);
    }

    [Fact]
    public async Task The_scan_changes_nothing_and_gives_the_same_answer_twice()
    {
        await AddAsync("Ann", "Lee", phone: Phone(1)); await AddAsync("Anne", "Lee", phone: Phone(2));
        int before;
        await using (var db = _fixture.CreateContext()) before = await db.Patients.CountAsync() + await db.AuditLogEntries.CountAsync();
        var first = await ScanAsync(); var second = await ScanAsync();
        Assert.Equal(first.Pairs.Select(p => (p.First.Id, p.Second.Id, p.Score, p.SuggestedSurvivorId)), second.Pairs.Select(p => (p.First.Id, p.Second.Id, p.Score, p.SuggestedSurvivorId)));
        await using var after = _fixture.CreateContext();
        Assert.Equal(before, await after.Patients.CountAsync() + await after.AuditLogEntries.CountAsync());
    }

    [Fact]
    public async Task The_scan_and_the_registration_check_give_the_same_reasons_for_the_same_pair()
    {
        var people = new[]
        {
            await AddAsync("Ann", "Lee", phone: "(555) 010-0100", email: "ann@example.test"),
            await AddAsync("Anne", "Lee", phone: "(555) 010-0100"),
            await AddAsync("Ann", "Lea", phone: Phone(2), email: "ANN@example.test"),
            await AddAsync("Bea", "Moss", phone: Phone(3)),
        };
        var pairs = (await ScanAsync()).Pairs;
        Assert.NotEmpty(pairs);
        await using var db = _fixture.CreateContext();
        var detector = new PatientDuplicateDetector(db);
        foreach (var pair in pairs)
        {
            var a = people.Single(p => p.Id == pair.First.Id);
            var input = new CleanPatient(a.FirstName, a.MiddleName, a.LastName, a.DateOfBirth, a.Sex, a.Phone, a.Email, a.AddressLine1, a.AddressLine2, a.City, a.State, a.PostalCode);
            var found = (await detector.FindAsync(input, a.Id, default)).Single(c => c.Id == pair.Second.Id);
            Assert.Equal(found.Reasons.Order(), pair.Reasons.Order());
        }
    }

    [Fact]
    public void The_rules_alone_a_same_name_pair_is_just_that_and_the_score_adds_the_weights_and_stops_at_a_hundred()
    {
        var a = new PatientFacts("Ann", "Lee", "(555) 010-0100", "ann@example.test");
        Assert.Equal(new[] { PatientMatchRules.SameNameReason }, PatientMatchRules.Reasons(a, a with { FirstName = "  ann ", LastName = "LEE" }));           // names are compared as the registration check does: trimmed, upper-cased
        Assert.Equal(100, PatientMatchRules.Score(PatientMatchRules.Reasons(a, a)));
        var all = PatientMatchRules.Reasons(a, new PatientFacts("Anne", "Lee", "555.010.0100", "ANN@example.test"));
        Assert.Equal(new[] { PatientMatchRules.SameLastNameReason, PatientMatchRules.SamePhoneReason, PatientMatchRules.SameEmailReason }, all);
        Assert.Equal(100, PatientMatchRules.Score(all));                                                                                                      // 40 + 30 + 30
        Assert.Equal(60, PatientMatchRules.Score([PatientMatchRules.SamePhoneReason, PatientMatchRules.SameEmailReason]));
        Assert.Empty(PatientMatchRules.Reasons(a, new PatientFacts("Bo", "Kim", "(555) 020-0200", null)));
    }
}
