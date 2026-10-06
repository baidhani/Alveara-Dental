using System.Net;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-013-C01 at the HTTP boundary: who may amend, resolve, reactivate and link a diagnosis (the same permissions as STORY-013's writes, plus CSRF), the row-version contract, the stable refusal shape for
/// coding, source, region, link and treatment-plan-state problems, coding and provenance surviving a real round trip, amendments and the lifecycle in the history, links to a finding and a chart of the
/// same patient only, and the treatment-plan forward reference staying unresolved through all of it. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class DiagnosisStructureApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo, _annEncounter;
    private int _findings;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        _annEncounter = e.Id;
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<Guid> FindingAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        var f = new ToothFinding { Id = Guid.NewGuid(), PatientId = patient, ToothKey = ToothKeys.All[_findings++ % 32], Condition = "Crown", ConditionScope = "WholeTooth", State = "Diagnosed", Status = "Active", CreatedAtUtc = DateTimeOffset.UtcNow };
        db.ToothFindings.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    private async Task<Guid> ChartAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        var e = new PerioExam { Id = Guid.NewGuid(), PatientId = patient, IdempotencyKey = Guid.NewGuid().ToString("N"), RecordedAtUtc = DateTimeOffset.UtcNow, RecordedByUserId = Guid.NewGuid(), ReadingCount = 1 };
        db.PerioExams.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private static string Id(JsonElement d) => d.GetProperty("id").GetString()!;
    private static string Version(JsonElement d) => d.GetProperty("rowVersion").GetString()!;
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string[] Problems(JsonElement e) => e.GetProperty("problems").EnumerateArray().Select(p => $"{p.GetProperty("field").GetString()}:{p.GetProperty("code").GetString()}").ToArray();
    private static string? Str(JsonElement e, string name) => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetString();

    private async Task<JsonElement> RecordAsync(Session by, object? extra = null, string key = "k1")
    {
        var body = new Dictionary<string, object?> { ["idempotencyKey"] = key, ["encounterId"] = _annEncounter, ["label"] = "Chronic periodontitis" };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(extra);
        var r = await by.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses", body);
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> Post(Session by, JsonElement d, string action, object body) =>
        by.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/{action}", body);

    // ---------- authentication, roles and CSRF ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_new_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        foreach (var action in new[] { "amend", "resolve", "reactivate", "links" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/diagnoses/{id}/{action}"))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_amend_resolve_reactivate_and_link(string role, bool allowed)
    {
        var d = await RecordAsync(await _api.SessionAsync("Dentist"));
        var finding = await FindingAsync(_ann);
        var by = await _api.SessionAsync(role);
        var calls = new[]
        {
            await Post(by, d, "amend", new { rowVersion = Version(d), codingSystem = "Local", code = "A1", reason = "Coded" }),
            await Post(by, d, "links", new { linkType = "Finding", targetId = finding }),
        };
        foreach (var c in calls) Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, c.Status);
        var current = (await by.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}")).Body;
        if (!allowed) { Assert.Equal(HttpStatusCode.Forbidden, (await Post(by, d, "resolve", new { rowVersion = Version(d), reason = "x" })).Status); Assert.Equal(HttpStatusCode.Forbidden, (await Post(by, d, "reactivate", new { rowVersion = Version(d), reason = "x" })).Status); }
        else Assert.Equal(HttpStatusCode.OK, (await Post(by, current, "resolve", new { rowVersion = Version(current), reason = "Healed" })).Status);
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_is_refused_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist);
        var finding = await FindingAsync(_ann);
        foreach (var (action, body) in new (string, object)[]
        {
            ("amend", new { rowVersion = Version(d), codingSystem = "Local", code = "A1", reason = "y" }), ("resolve", new { rowVersion = Version(d), reason = "y" }),
            ("reactivate", new { rowVersion = Version(d), reason = "y" }), ("links", new { linkType = "Finding", targetId = finding }),
        })
            Assert.NotEqual(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/{action}", body, csrf: false)).Status);
        var after = (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}")).Body;
        Assert.Equal((Version(d), 0), (Version(after), after.GetProperty("links").GetArrayLength()));
    }

    // ---------- coding and provenance round trip ----------

    [Fact]
    public async Task Coding_source_and_region_round_trip_over_http_with_no_coding_system_needed()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plain = await RecordAsync(dentist, key: "plain");
        Assert.Equal((null, null, "Manual", null, null, "Active"), (Str(plain, "codingSystem"), Str(plain, "code"), Str(plain, "source"), Str(plain, "sourceNote"), Str(plain, "regionKey"), Str(plain, "status")));

        var coded = await RecordAsync(dentist, new { CodingSystem = "ICD-10-CM", Code = "K05.311", Source = "Imported", SourceNote = "From the old chart", RegionKey = "LowerArch" }, key: "coded");
        Assert.Equal(("ICD-10-CM", "K05.311", "Imported", "From the old chart", "LowerArch"), (Str(coded, "codingSystem"), Str(coded, "code"), Str(coded, "source"), Str(coded, "sourceNote"), Str(coded, "regionKey")));
        var list = (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/diagnoses")).Body.GetProperty("diagnoses");
        Assert.Equal(2, list.GetArrayLength());
        var history = (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(coded)}/history")).Body.GetProperty("versions");
        Assert.Equal(("ICD-10-CM", "K05.311", "Imported", "LowerArch"), (Str(history[0], "codingSystem"), Str(history[0], "code"), Str(history[0], "source"), Str(history[0], "regionKey")));
    }

    [Fact]
    public async Task Incorrect_coding_source_region_or_treatment_plan_state_is_a_400_listing_every_problem_and_nothing_is_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses", new
        {
            idempotencyKey = "bad", encounterId = _annEncounter, label = "Perio", toothKey = "16", treatmentPlanReference = "plan-1", codingSystem = "SNOMED", code = (string?)null,
            source = "Wrong", sourceNote = "x", regionKey = "Nowhere", treatmentPlanReferenceState = "Resolved",
        });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
        foreach (var expected in new[] { "codingSystem:unsupported_system", "code:coding_incomplete", "source:unsupported_source", "regionKey:unknown_region", "treatmentPlanReferenceState:not_supported" })
            Assert.Contains(expected, Problems(r.Body));
        Assert.Equal(0, (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/diagnoses?includeWithdrawn=true")).Body.GetProperty("diagnoses").GetArrayLength());
    }

    // ---------- amendment, lifecycle, links ----------

    [Fact]
    public async Task The_workflow_end_to_end_record_amend_link_resolve_reactivate_withdraw_with_every_step_in_the_history_and_the_plan_reference_unresolved_throughout()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var d = await RecordAsync(dentist, new { ToothKey = "16", TreatmentPlanReference = "plan-2026-07" });

        var amend = await Post(hygienist, d, "amend", new { rowVersion = Version(d), toothKey = (string?)null, regionKey = "UpperRight", codingSystem = "Local", code = "P-9", source = "Mapped", sourceNote = "Mapped from old", reason = "Moved to the region and coded" });
        Assert.Equal(HttpStatusCode.OK, amend.Status);
        Assert.Equal((null, "UpperRight", "Local", "P-9", "Mapped"), (Str(amend.Body, "toothKey"), Str(amend.Body, "regionKey"), Str(amend.Body, "codingSystem"), Str(amend.Body, "code"), Str(amend.Body, "source")));
        Assert.Equal(("plan-2026-07", "Unresolved"), (Str(amend.Body, "treatmentPlanReference"), Str(amend.Body, "treatmentPlanReferenceState")));

        var finding = await FindingAsync(_ann);
        var chart = await ChartAsync(_ann);
        var linked = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/links", new { linkType = "Finding", targetId = finding });
        linked = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/links", new { linkType = "PerioExam", targetId = chart });
        Assert.Equal(new[] { "Finding", "PerioExam" }, linked.Body.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("linkType").GetString()));
        Assert.Equal(2, (await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/links", new { linkType = "Finding", targetId = finding })).Body.GetProperty("links").GetArrayLength());   // the repeat adds nothing

        var current = (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}")).Body;
        var resolved = await Post(dentist, current, "resolve", new { rowVersion = Version(current), reason = "Healed" });
        Assert.Equal("Resolved", Str(resolved.Body, "status"));
        Assert.Equal(1, (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/diagnoses")).Body.GetProperty("diagnoses").GetArrayLength());
        var back = await Post(dentist, resolved.Body, "reactivate", new { rowVersion = Version(resolved.Body), reason = "Came back" });
        Assert.Equal("Active", Str(back.Body, "status"));
        var withdrawn = await Post(dentist, back.Body, "withdraw", new { rowVersion = Version(back.Body), reason = "Entered on the wrong patient" });
        Assert.Equal("Withdrawn", Str(withdrawn.Body, "status"));

        var versions = (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}/history")).Body.GetProperty("versions");
        Assert.Equal(new[] { "Recorded", "Amended", "Resolved", "Reactivated", "Withdrawn" }, versions.EnumerateArray().Select(v => v.GetProperty("changeType").GetString()));
        Assert.All(versions.EnumerateArray(), v => Assert.Equal(("plan-2026-07", "Unresolved"), (Str(v, "treatmentPlanReference"), Str(v, "treatmentPlanReferenceState"))));
        Assert.Equal(("16", "UpperRight"), (Str(versions[0], "toothKey") ?? "16", Str(versions[1], "regionKey")));
        Assert.Equal(new[] { null, "Moved to the region and coded", "Healed", "Came back", "Entered on the wrong patient" }, versions.EnumerateArray().Select(v => Str(v, "reason")));
    }

    [Fact]
    public async Task Refusals_have_stable_codes_a_withdrawn_diagnosis_is_409_a_stale_amendment_is_409_a_missing_reason_is_400_and_a_bad_link_is_404_or_400()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist);

        var noReason = await Post(dentist, d, "amend", new { rowVersion = Version(d), codingSystem = "Local", code = "A1" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (noReason.Status, Error(noReason.Body)));
        Assert.Contains("reason:required", Problems(noReason.Body));
        var noVersion = await Post(dentist, d, "amend", new { codingSystem = "Local", code = "A1", reason = "x" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var resolveNoReason = await Post(dentist, d, "resolve", new { rowVersion = Version(d) });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (resolveNoReason.Status, Error(resolveNoReason.Body)));

        var amended = await Post(dentist, d, "amend", new { rowVersion = Version(d), codingSystem = "Local", code = "A1", reason = "Coded" });
        Assert.Equal(HttpStatusCode.OK, amended.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(dentist, d, "amend", new { rowVersion = Version(d), codingSystem = "Local", code = "B2", reason = "Late" })).Status);     // stale

        Assert.Equal((HttpStatusCode.NotFound, "link_target_not_found"), await LinkRefusal(dentist, d, "Finding", await FindingAsync(_bo)));
        Assert.Equal((HttpStatusCode.NotFound, "link_target_not_found"), await LinkRefusal(dentist, d, "PerioExam", await ChartAsync(_bo)));
        Assert.Equal((HttpStatusCode.NotFound, "link_target_not_found"), await LinkRefusal(dentist, d, "Finding", Guid.NewGuid()));
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), await LinkRefusal(dentist, d, "TreatmentPlan", Guid.NewGuid()));

        var withdrawn = await Post(dentist, amended.Body, "withdraw", new { rowVersion = Version(amended.Body), reason = "Wrong patient" });
        var finding = await FindingAsync(_ann);
        foreach (var (action, body) in new (string, object)[]
        {
            ("amend", new { rowVersion = Version(withdrawn.Body), codingSystem = "Local", code = "C3", reason = "x" }), ("resolve", new { rowVersion = Version(withdrawn.Body), reason = "x" }),
            ("reactivate", new { rowVersion = Version(withdrawn.Body), reason = "x" }), ("links", new { linkType = "Finding", targetId = finding }),
        })
        {
            var r = await Post(dentist, withdrawn.Body, action, body);
            Assert.Equal((HttpStatusCode.Conflict, "diagnosis_withdrawn"), (r.Status, Error(r.Body)));
        }
        Assert.Equal((HttpStatusCode.NotFound, "diagnosis_not_found"), await Missing(dentist));
    }

    private static async Task<(HttpStatusCode, string)> LinkRefusal(Session by, JsonElement d, string type, Guid target)
    {
        var r = await by.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/links", new { linkType = type, targetId = target });
        return (r.Status, Error(r.Body));
    }

    private static async Task<(HttpStatusCode, string)> Missing(Session by)
    {
        var r = await by.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Guid.NewGuid()}/links", new { linkType = "Finding", targetId = Guid.NewGuid() });
        return (r.Status, Error(r.Body));
    }

    [Fact]
    public async Task A_request_to_mark_the_treatment_plan_reference_resolved_is_a_400_on_correct_and_amend_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist, new { TreatmentPlanReference = "plan-1" });
        var correct = await Post(dentist, d, "correct", new { rowVersion = Version(d), label = "Chronic periodontitis", reason = "x", treatmentPlanReferenceState = "Resolved" });
        var amend = await Post(dentist, d, "amend", new { rowVersion = Version(d), codingSystem = "Local", code = "A1", reason = "x", treatmentPlanReferenceState = "Validated" });
        foreach (var r in new[] { correct, amend })
        {
            Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
            Assert.Contains("treatmentPlanReferenceState:not_supported", Problems(r.Body));
        }
        var after = (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}")).Body;
        Assert.Equal((Version(d), "plan-1", "Unresolved"), (Version(after), Str(after, "treatmentPlanReference"), Str(after, "treatmentPlanReferenceState")));
    }

    [Fact]
    public async Task Story_013s_own_correct_and_withdraw_contract_is_unchanged_for_a_diagnosis_that_has_structure()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist, new { CodingSystem = "Local", Code = "A1", TreatmentPlanReference = "plan-1" });
        var corrected = await Post(dentist, d, "correct", new { rowVersion = Version(d), label = "Generalized gingivitis", reason = "Clarified" });
        Assert.Equal(HttpStatusCode.OK, corrected.Status);
        Assert.Equal(("Generalized gingivitis", "Local", "A1", "plan-1"), (Str(corrected.Body, "label"), Str(corrected.Body, "codingSystem"), Str(corrected.Body, "code"), Str(corrected.Body, "treatmentPlanReference")));
        var gone = await Post(dentist, corrected.Body, "withdraw", new { rowVersion = Version(corrected.Body), reason = "Wrong patient" });
        Assert.Equal(("Withdrawn", "plan-1"), (Str(gone.Body, "status"), Str(gone.Body, "treatmentPlanReference")));
    }
}
