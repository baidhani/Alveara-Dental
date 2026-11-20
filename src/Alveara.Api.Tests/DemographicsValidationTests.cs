using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Patients;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N025 slice 4 (F-032, F-033; owner decisions DEM-2..DEM-4) against real SQL Server:
/// - the lists: Sex (any letter case stored as listed), Country (ISO alpha-2, any case; blank = the practice's default, else US), the US State list (a code or full name in
///   any case stored as the code; the armed-forces codes accepted), other countries' free text; a value not on its list is refused with a message per field;
/// - an edit holds a value to its list only when it is changed: an unchanged old value is kept so another field can be corrected, a changed one is checked; an edit
///   without a country keeps the patient's own;
/// - the review list names the patients with an off-list stored value and only EditPatients may read it; the options are readable;
/// - the practice's default country: a code from the list, refused otherwise.
/// </summary>
public class DemographicsValidationTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private PatientTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Session _admin = null!;
    private Session _billing = null!;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new PatientTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await new SchedulingTestSupport(_fixture).ArrangeAsync();
        _admin = await _api.SessionAsync("Admin");
        _billing = await _api.SessionAsync("Billing");
    });

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<Patient> RegisterAsync(RegisterPatientRequest request)
    {
        await using var db = _fixture.CreateContext();
        return (await _s.Registration(db).RegisterAsync(request, Guid.NewGuid().ToString("N"), _s.Actor, default)).Patient;
    }

    private async Task<PatientException> RefusedAsync(RegisterPatientRequest request)
    {
        await using var db = _fixture.CreateContext();
        return await Assert.ThrowsAnyAsync<PatientException>(() => _s.Registration(db).RegisterAsync(request, Guid.NewGuid().ToString("N"), _s.Actor, default));
    }

    private static int _n;

    /// <summary>A different person each time: own surname and phone, so no duplicate rule is involved in a list test.</summary>
    private static RegisterPatientRequest Person(string first, string? sex = "Female", string state = "TX", string? country = null)
    {
        var n = Interlocked.Increment(ref _n);
        return PatientTestSupport.Request(first, $"Demo{first}", "1980-02-02", $"(555) 4{n / 10000 % 100:00}-{n % 10000:0000}") with { Sex = sex, State = state, Country = country };
    }

    private async Task SqlAsync(string sql) { await using var db = _fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }

    private async Task<Patient> EditAsync(Patient p, Func<PatientFields, PatientFields> change)
    {
        await using var db = _fixture.CreateContext();
        var now = await db.Patients.AsNoTracking().SingleAsync(x => x.Id == p.Id);
        var fields = new PatientFields(now.FirstName, now.MiddleName, now.LastName, now.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), now.Sex, now.Phone, now.Email,
            now.AddressLine1, now.AddressLine2, now.City, now.State, now.PostalCode, now.Country);
        return await new PatientEditService(db, PatientTestSupport.Clock).UpdateAsync(p.Id, change(fields), Convert.ToBase64String(now.RowVersion), _s.Actor, default);
    }

    [Theory]
    [InlineData(" female ", "Female")]
    [InlineData("MALE", "Male")]
    [InlineData("unknown/UNSPECIFIED", "Unknown/unspecified")]
    [InlineData("Other", "Other")]
    public async Task Sex_is_one_of_the_list_and_stored_as_listed(string typed, string stored) =>
        Assert.Equal(stored, (await RegisterAsync(Person($"S{stored.Length}{typed.Length}", typed))).Sex);

    [Theory]
    [InlineData("F")]
    [InlineData("Woman")]
    [InlineData("Fe male")]
    public async Task A_sex_not_on_the_list_is_refused(string typed) =>
        Assert.True((await RefusedAsync(Person("Refused", typed))).FieldErrors.ContainsKey("sex"));

    [Theory]
    [InlineData("tx", "TX")]
    [InlineData("  new   york ", "NY")]
    [InlineData("District of Columbia", "DC")]
    [InlineData("pr", "PR")]
    [InlineData("AE", "AE")]
    public async Task A_us_state_is_a_code_from_the_list_and_a_full_name_is_stored_as_its_code(string typed, string code)
    {
        var p = await RegisterAsync(Person($"St{code}", state: typed));
        Assert.Equal((code, "US"), (p.State, p.Country));
    }

    [Theory]
    [InlineData("Ill.")]
    [InlineData("Texass")]
    [InlineData("XX")]
    public async Task A_us_state_not_on_the_list_is_refused(string typed) =>
        Assert.True((await RefusedAsync(Person("BadState", state: typed))).FieldErrors.ContainsKey("state"));

    [Fact]
    public async Task Another_country_takes_free_text_and_a_country_must_be_an_iso_code()
    {
        var p = await RegisterAsync(Person("Toronto", state: "Ontario", country: "ca"));
        Assert.Equal(("CA", "Ontario"), (p.Country, p.State));
        Assert.True((await RefusedAsync(Person("NoLand", country: "Canada"))).FieldErrors.ContainsKey("country"));
        Assert.True((await RefusedAsync(Person("NoLand2", country: "ZZ"))).FieldErrors.ContainsKey("country"));
    }

    [Fact]
    public async Task A_patient_without_a_country_takes_the_practice_default_else_the_us()
    {
        Assert.Equal("US", (await RegisterAsync(Person("NoCountry1"))).Country);
        await using (var db = _fixture.CreateContext())
            await new PracticeConfigurationService(db, PatientTestSupport.Clock).SavePracticeAsync("Alder Dental", null, null, await RowVersionAsync(), _s.Actor, default, "gb");
        var p = await RegisterAsync(Person("NoCountry2", state: "Kent"));
        Assert.Equal(("GB", "Kent"), (p.Country, p.State));                                                // not the US, so the state is free text
        await using (var db = _fixture.CreateContext())
            await Assert.ThrowsAsync<ConfigurationException>(() => new PracticeConfigurationService(db, PatientTestSupport.Clock).SavePracticeAsync("Alder Dental", null, null, null, _s.Actor, default, "Great Britain"));
    }

    private async Task<string?> RowVersionAsync()
    {
        await using var db = _fixture.CreateContext();
        var row = await db.PracticeSettings.AsNoTracking().SingleOrDefaultAsync();
        return row is null ? null : Convert.ToBase64String(row.RowVersion);
    }

    [Fact]
    public async Task An_edit_keeps_an_unchanged_old_value_checks_a_changed_one_and_keeps_the_country_when_none_is_sent()
    {
        var p = await RegisterAsync(Person("OldValues"));
        await SqlAsync($"UPDATE [Patients] SET [Sex] = N'F', [State] = N'Ill.' WHERE [Id] = '{p.Id}'");          // stored before the lists existed

        var edited = await EditAsync(p, f => f with { Phone = "(555) 222-3333" });                           // another field: saved, the old values kept
        Assert.Equal(("(555) 222-3333", "F", "Ill."), (edited.Phone, edited.Sex, edited.State));

        await using (var db = _fixture.CreateContext())
        {
            var refused = await Assert.ThrowsAsync<PatientException>(() => EditAsync(p, f => f with { Sex = "W" }));
            Assert.True(refused.FieldErrors.ContainsKey("sex"));                                              // a changed value is held to the list
        }
        var fixedUp = await EditAsync(p, f => f with { Sex = "female", State = "illinois", Country = null });   // corrected; no country sent: the patient's own kept
        Assert.Equal(("Female", "IL", "US"), (fixedUp.Sex, fixedUp.State, fixedUp.Country));
    }

    [Fact]
    public async Task The_review_list_names_off_list_values_and_only_editors_may_read_it()
    {
        var off = await RegisterAsync(Person("OffList"));
        var fine = await RegisterAsync(Person("OnList"));
        await SqlAsync($"UPDATE [Patients] SET [Sex] = N'female', [State] = N'Ill.' WHERE [Id] = '{off.Id}'");   // "female" is off-list as STORED (exact value)

        var response = await _admin.Client.GetAsync("/api/patients/demographic-review");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => (Id: i.GetProperty("patientId").GetGuid(), Field: i.GetProperty("field").GetString(), Value: i.GetProperty("value").GetString())).ToList();
        Assert.Contains((off.Id, "sex", "female"), items);
        Assert.Contains((off.Id, "state", "Ill."), items);
        Assert.DoesNotContain(items, i => i.Id == fine.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await _billing.Client.GetAsync("/api/patients/demographic-review")).StatusCode);
        var options = JsonDocument.Parse(await _admin.Client.GetStringAsync("/api/patients/demographic-options")).RootElement;
        Assert.Equal(4, options.GetProperty("sexValues").GetArrayLength());
        Assert.Equal(59, options.GetProperty("usStates").GetArrayLength());
        Assert.Equal(249, options.GetProperty("countries").GetArrayLength());
        Assert.Equal("US", options.GetProperty("defaultCountry").GetString());
    }
}
