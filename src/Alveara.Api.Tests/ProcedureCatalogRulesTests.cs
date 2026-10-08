using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N005 against real SQL Server: what the procedure catalog accepts and refuses. A procedure is created with a stable identity, a code in an explicit code system, a description, a category,
/// where it applies and a fee; every wrong entry is refused with a message per field in one answer; local, CDT and external codes keep their own shapes and provenance (and a local code can never
/// pass for CDT); identity (code and code system) never changes; a duplicate code is refused while an identical repeat is quiet, even when two people add it at the same moment.
/// </summary>
public class ProcedureCatalogRulesTests : ProcedureTestBase
{
    // ---------- creating ----------

    [Fact]
    public async Task A_procedure_is_created_with_identity_code_system_description_category_applicability_and_fee()
    {
        var created = await CreateAsync(Input("LOCAL-100", "Local", "Adult cleaning", "Preventive", "WholeMouth", fee: 95.5m));
        var s = created.Summary;
        Assert.NotEqual(Guid.Empty, s.Id);
        Assert.Equal(("Local", "LOCAL-100", true, "Active", 1), (s.CodeSystem, s.Code, s.IsActive, s.Status, s.CurrentVersionNumber));
        var v = Assert.Single(created.Versions);
        Assert.Equal((1, "Adult cleaning", "Preventive", "WholeMouth", "Both", 95.5m, "USD", Today), (v.VersionNumber, v.Description, v.Category, v.Scope, v.Dentition, v.Fee, v.Currency, v.EffectiveFrom));
        Assert.Equal("Dr. Okafor", v.CreatedByName);
        Assert.False(string.IsNullOrEmpty(s.RowVersion));
    }

    [Fact]
    public async Task Every_missing_or_wrong_field_is_named_in_one_refusal_and_nothing_is_stored()
    {
        var e = await Refused(() => CreateAsync(new ProcedureInput(null, null, null, null, null, null, null, null, null, null, null)));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        foreach (var field in new[] { "codeSystem", "code", "description", "category", "scope", "fee" }) Assert.True(e.FieldErrors.ContainsKey(field), $"{field} should be named");

        Assert.Empty(await With(s => s.ListAsync(null, null, null, null, null, default)));
    }

    [Theory]
    [InlineData("Local", "D1234", "CDT")]        // a local code that looks like CDT is refused so it cannot pass for one
    [InlineData("Local", "X", "2 to 20")]
    [InlineData("Local", "has space", "2 to 20")]
    [InlineData("Local", "TOO-LONG-CODE-0123456789", "2 to 20")]
    [InlineData("CDT", "D12", "letter D and four digits")]
    [InlineData("CDT", "X1234", "letter D and four digits")]
    [InlineData("External", "bad code!", "letters, digits")]
    public async Task A_code_that_does_not_fit_its_code_system_is_refused_with_a_message_that_says_why(string system, string code, string messagePart)
    {
        var e = await Refused(() => CreateAsync(Input(code, system, sourceName: system == "External" ? "State schedule" : null)));
        Assert.Contains(messagePart, e.FieldErrors["code"]);
    }

    [Theory]
    [InlineData(-0.01, "negative")]
    [InlineData(1000000.01, "more than")]
    [InlineData(12.345, "two decimal")]
    public async Task An_invalid_fee_is_refused(double fee, string messagePart)
    {
        var e = await Refused(() => CreateAsync(Input(fee: (decimal)fee)));
        Assert.Contains(messagePart, e.FieldErrors["fee"]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(12.5)]
    [InlineData(1000000.0)]
    public async Task The_fee_boundaries_are_accepted_zero_for_no_charge_up_to_the_limit(double fee)
    {
        var created = await CreateAsync(Input(code: $"FEE-{(int)fee}", fee: (decimal)fee));
        Assert.Equal((decimal)fee, created.Versions[0].Fee);
    }

    [Fact]
    public async Task A_description_must_be_present_short_enough_and_free_of_control_characters()
    {
        Assert.Contains("required", (await Refused(() => CreateAsync(Input(description: "   ")))).FieldErrors["description"]);
        Assert.Contains("200", (await Refused(() => CreateAsync(Input(description: new string('x', 201))))).FieldErrors["description"]);
        Assert.Contains("control", (await Refused(() => CreateAsync(Input(description: "Bad\u0007text")))).FieldErrors["description"]);
        var longest = await CreateAsync(Input(description: new string('x', 200)));
        Assert.Equal(200, longest.Versions[0].Description.Length);
    }

    [Fact]
    public async Task Category_scope_and_dentition_must_be_from_the_allowed_lists_and_dates_must_make_sense()
    {
        Assert.True((await Refused(() => CreateAsync(Input(category: "Cosmetic")))).FieldErrors.ContainsKey("category"));
        Assert.True((await Refused(() => CreateAsync(Input(scope: "Mouth")))).FieldErrors.ContainsKey("scope"));
        Assert.True((await Refused(() => CreateAsync(Input(scope: "Tooth", dentition: "Adult")))).FieldErrors.ContainsKey("dentition"));
        Assert.True((await Refused(() => CreateAsync(Input(effectiveFrom: Today, validThrough: Today.AddDays(-1))))).FieldErrors.ContainsKey("validThrough"));
        Assert.True((await Refused(() => CreateAsync(Input(effectiveFrom: Today.AddYears(4))))).FieldErrors.ContainsKey("effectiveFrom"));
        Assert.True((await Refused(() => CreateAsync(Input(effectiveFrom: new DateOnly(1999, 12, 31))))).FieldErrors.ContainsKey("effectiveFrom"));
    }

    [Fact]
    public async Task Dentition_only_matters_for_tooth_level_work_and_is_stored_as_Both_otherwise()
    {
        var mouth = await CreateAsync(Input("WHOLE-1", scope: "WholeMouth", dentition: "Primary"));
        Assert.Equal("Both", mouth.Versions[0].Dentition);
        var tooth = await CreateAsync(Input("TOOTH-1", scope: "Tooth", dentition: "Primary"));
        Assert.Equal("Primary", tooth.Versions[0].Dentition);
    }

    // ---------- code system, source, version and provenance ----------

    [Fact]
    public async Task A_CDT_code_keeps_its_source_and_edition_and_a_local_code_has_none()
    {
        var cdt = await CreateAsync(Input("d1234", "CDT", "Practice wording for this code", "Preventive", sourceName: "Licensed code set held by the practice", sourceVersion: "2026"));
        Assert.Equal("D1234", cdt.Summary.Code);                         // upper-cased
        Assert.Equal(("Licensed code set held by the practice", "2026"), (cdt.Versions[0].SourceName, cdt.Versions[0].SourceVersion));

        var local = await CreateAsync(Input("LOCAL-200"));
        Assert.Equal((null, null), (local.Versions[0].SourceName, local.Versions[0].SourceVersion));
        Assert.Contains("no external source", (await Refused(() => CreateAsync(Input("LOCAL-201", sourceName: "Somewhere")))).FieldErrors["sourceName"]);
    }

    [Fact]
    public async Task An_external_code_set_must_name_its_source()
    {
        var e = await Refused(() => CreateAsync(Input("EXT-1", "External")));
        Assert.Contains("Name the source", e.FieldErrors["sourceName"]);
        var ok = await CreateAsync(Input("EXT-1", "External", sourceName: "State Medicaid schedule", sourceVersion: "FY2030"));
        Assert.Equal("External", ok.Summary.CodeSystem);
    }

    [Theory]
    [InlineData("CDT", "D1234")]
    [InlineData("External", "EXT-9")]
    public async Task Every_non_local_code_system_must_name_its_source_missing_or_blank_is_refused_and_nothing_is_stored(string system, string code)
    {
        var missing = await Refused(() => CreateAsync(Input(code, system)));
        Assert.Equal(("validation_failed", 400), (missing.Code, missing.StatusCode));
        Assert.Contains("Name the source", missing.FieldErrors["sourceName"]);
        Assert.Contains("Name the source", (await Refused(() => CreateAsync(Input(code, system, sourceName: "   ", sourceVersion: "2026")))).FieldErrors["sourceName"]);   // an edition alone is not a source
        Assert.Empty(await With(s => s.ListAsync(null, null, null, null, null, default)));
    }

    [Fact]
    public async Task A_local_code_accepts_neither_a_source_name_nor_an_edition()
    {
        Assert.Contains("no external source", (await Refused(() => CreateAsync(Input("LOCAL-300", sourceVersion: "2026")))).FieldErrors["sourceName"]);
        Assert.Contains("no external source", (await Refused(() => CreateAsync(Input("LOCAL-301", sourceName: "Somewhere", sourceVersion: "2026")))).FieldErrors["sourceName"]);
    }

    [Fact]
    public async Task The_source_edition_is_optional_for_a_CDT_or_external_code_but_the_source_name_is_not()
    {
        var cdt = await CreateAsync(Input("D3000", "CDT", sourceName: "Licensed set"));                 // no edition given
        Assert.Equal(("Licensed set", null), (cdt.Versions[0].SourceName, cdt.Versions[0].SourceVersion));
        var ext = await CreateAsync(Input("EXT-10", "External", sourceName: "State schedule"));
        Assert.Null(ext.Versions[0].SourceVersion);
    }

    [Fact]
    public async Task A_new_version_of_a_CDT_code_cannot_drop_its_source()
    {
        var v1 = await CreateAsync(Input("D4000", "CDT", sourceName: "Licensed set", sourceVersion: "2025"));
        var e = await Refused(() => ReviseAsync(v1, Like(v1, fee: 70m) with { SourceName = null, SourceVersion = null }));
        Assert.Contains("Name the source", e.FieldErrors["sourceName"]);
        Assert.Single((await With(s => s.GetAsync(v1.Summary.Id, null, default))).Versions);
    }

    [Fact]
    public async Task The_same_code_in_two_code_systems_is_two_procedures()
    {
        var cdt = await CreateAsync(Input("D5000", "CDT", sourceName: "Licensed set"));
        var ext = await CreateAsync(Input("D5000", "External", sourceName: "Another schedule"));
        Assert.NotEqual(cdt.Summary.Id, ext.Summary.Id);
    }

    [Fact]
    public async Task A_new_edition_of_the_source_is_a_new_version_of_the_same_procedure_never_a_new_identity()
    {
        var v1 = await CreateAsync(Input("D2000", "CDT", sourceName: "Licensed set", sourceVersion: "2025"));
        var v2 = await ReviseAsync(v1, Like(v1, sourceVersion: "2026"), "Code set update", Mar15);

        Assert.Equal(v1.Summary.Id, v2.Summary.Id);
        Assert.Equal(("D2000", "CDT", 2), (v2.Summary.Code, v2.Summary.CodeSystem, v2.Summary.CurrentVersionNumber));
        Assert.Equal(new[] { "2025", "2026" }, v2.Versions.Select(v => v.SourceVersion));   // the old edition is still readable on the old version
    }

    // ---------- identity is fixed ----------

    [Fact]
    public async Task The_code_and_code_system_of_an_existing_procedure_cannot_be_changed()
    {
        var created = await CreateAsync();
        var otherCode = await Refused(() => ReviseAsync(created, Like(created) with { Code = "LOCAL-999" }));
        Assert.Equal(("identity_fixed", 400), (otherCode.Code, otherCode.StatusCode));
        var otherSystem = await Refused(() => ReviseAsync(created, Like(created) with { CodeSystem = "External", SourceName = "X" }));
        Assert.Equal("identity_fixed", otherSystem.Code);
        Assert.Equal(1, (await With(s => s.GetAsync(created.Summary.Id, null, default))).Summary.CurrentVersionNumber);
    }

    // ---------- duplicates ----------

    [Fact]
    public async Task A_different_procedure_with_a_used_code_is_refused_and_an_identical_repeat_is_quiet()
    {
        var first = await CreateAsync();
        var repeat = await CreateAsync(Input());
        Assert.Equal(first.Summary.Id, repeat.Summary.Id);
        Assert.Single(await With(s => s.ListAsync(null, null, null, null, null, default)));

        var clash = await Refused(() => CreateAsync(Input(description: "A different procedure", fee: 75m)));
        Assert.Equal(("procedure_exists", 409), (clash.Code, clash.StatusCode));
        Assert.True(clash.FieldErrors.ContainsKey("code"));

        var lower = await CreateAsync(Input(code: "local-100"));     // codes are compared ignoring case
        Assert.Equal(first.Summary.Id, lower.Summary.Id);
    }

    [Fact]
    public async Task Two_people_adding_the_same_code_at_the_same_moment_end_with_one_procedure()
    {
        var attempts = Enumerable.Range(0, 4).Select(_ => Task.Run(() => CreateAsync(Input("RACE-1")))).ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results.Select(r => r.Summary.Id).Distinct());
        await using var db = _fixtureDb();
        Assert.Equal(1, await db.ProcedureDefinitions.CountAsync(p => p.Code == "RACE-1"));
        Assert.Equal(1, await db.ProcedureEvents.CountAsync(e => e.ChangeType == "Created"));
    }

    private AlveraDbContext _fixtureDb() => Fixture.CreateContext();
}
