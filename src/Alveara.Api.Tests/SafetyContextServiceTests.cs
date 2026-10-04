using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Safety;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N011 shared safety context, against real SQL Server: what downstream clinical screens (odontogram, perio, diagnosis, planning, completion, prescriptions) read before they act.
/// It is a read-only projection of what is stored - active allergies and medications from the clinical record with their provenance, the clinician's alerts, open clearances, and the
/// gaps (what is not established) - and nothing is inferred or invented. The minimal board indicator is tested here for what it can and cannot reveal.
/// </summary>
public class SafetyContextServiceTests : SafetyTestBase
{
    private ISafetyContextProvider Provider(Alveara.Api.Data.AlveraDbContext db) => new SafetyContextService(db, Clock);

    [Fact]
    public async Task Active_allergies_and_current_medications_come_from_the_clinical_record_with_their_provenance()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives", severity: EncounterSeverities.Severe);
        await RecordItemAsync(EncounterEntryKinds.Medication, "Lisinopril", dose: "10 mg", frequency: "daily");
        var c = await WithDb(db => Provider(db).GetAsync(Ann, S.Actor, default));   // through the interface downstream screens use

        var allergy = Assert.Single(c.Entries, e => e.Category == SafetyCategories.Allergy);
        Assert.Equal((SafetyOrigins.ClinicalRecord, "Penicillin", "Hives", SafetySeverities.High, SafetyAlertStatuses.Active), (allergy.Origin, allergy.Title, allergy.Detail, allergy.Severity, allergy.Status));
        Assert.Equal(("Clinical record: allergies", "Dr. Okafor"), (allergy.Source, allergy.LastUpdatedByName));
        Assert.NotNull(allergy.SourceItemId);
        var med = Assert.Single(c.Entries, e => e.Category == SafetyCategories.Medication);
        Assert.Equal(("Lisinopril", "10 mg · daily", (string?)null, "Clinical record: medications"), (med.Title, med.Detail, med.Severity, med.Source));
        Assert.Equal((0, 1, 1), (c.Summary.ActiveAlertCount, c.Summary.ActiveAllergyCount, c.Summary.CurrentMedicationCount));
        Assert.Equal(SafetySeverities.High, c.Summary.HighestSeverity);
    }

    [Fact]
    public async Task Resolved_inactive_discontinued_and_removed_record_items_are_not_shown_as_active_safety_information()
    {
        var resolved = await RecordItemAsync(EncounterEntryKinds.Allergy, "Latex");
        var stopped = await RecordItemAsync(EncounterEntryKinds.Medication, "Warfarin");
        var wrong = await RecordItemAsync(EncounterEntryKinds.Allergy, "Shellfish");
        await RecordItemAsync(EncounterEntryKinds.MedicalHistory, "Asthma");           // history is not an allergy or a medication
        await WithDb(db => new ClinicalRecordService(db, Clock).SetStatusAsync(resolved.Id, ClinicalItemStatuses.Resolved, Convert.ToBase64String(resolved.RowVersion), null, null, S.Actor, default));
        await WithDb(db => new ClinicalRecordService(db, Clock).SetStatusAsync(stopped.Id, ClinicalItemStatuses.Discontinued, Convert.ToBase64String(stopped.RowVersion), null, null, S.Actor, default));
        await WithDb(db => new ClinicalRecordService(db, Clock).RemoveInErrorAsync(wrong.Id, Convert.ToBase64String(wrong.RowVersion), "Wrong chart", null, S.Actor, default));
        Assert.Empty((await ContextAsync()).Entries);
    }

    [Theory]
    [InlineData("Severe", "High")]
    [InlineData("Moderate", "Moderate")]
    [InlineData("Mild", "Low")]
    [InlineData(null, null)]
    public async Task An_allergys_recorded_severity_maps_to_the_safety_scale_and_an_unrecorded_one_is_flagged_never_guessed(string? recorded, string? expected)
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", severity: recorded);
        var e = Assert.Single((await ContextAsync()).Entries);
        Assert.Equal(expected, e.Severity);
        Assert.Equal(recorded is null, e.NeedsAttention);
        if (recorded is null) Assert.Contains("not recorded", e.AttentionReason);
    }

    [Fact]
    public async Task What_is_not_established_is_reported_as_a_gap_in_words_and_what_is_reviewed_produces_none()
    {
        var gaps = (await ContextAsync()).Gaps;
        Assert.Equal(new[] { EncounterEntryKinds.Allergy, EncounterEntryKinds.Medication, EncounterEntryKinds.MedicalHistory }, gaps.Select(g => g.Section));
        Assert.All(gaps, g => Assert.Equal(ClinicalSectionStatuses.NotReviewed, g.Status));

        await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown);
        await RecordReviewAsync(EncounterEntryKinds.Medication, ClinicalReviewStates.Unknown);
        await RecordItemAsync(EncounterEntryKinds.MedicalHistory, "Asthma");
        var c = await ContextAsync();
        Assert.Equal(new[] { (EncounterEntryKinds.Medication, ClinicalSectionStatuses.Unknown), (EncounterEntryKinds.MedicalHistory, ClinicalSectionStatuses.NeedsReview) }, c.Gaps.Select(g => (g.Section, g.Status)));
        Assert.Contains("could not be established", c.Gaps[0].Message);
        Assert.Empty(c.Entries);                                          // "none known" and "unknown" are not entries, and certainly not alerts
        Assert.Empty(c.Resolved);

        await RecordReviewAsync(EncounterEntryKinds.MedicalHistory, ClinicalReviewStates.Reviewed);
        Assert.DoesNotContain((await ContextAsync()).Gaps, g => g.Section == EncounterEntryKinds.MedicalHistory);
    }

    [Fact]
    public async Task Alerts_clearances_and_record_entries_are_merged_most_urgent_first_and_resolved_alerts_stay_visible_with_who_and_why()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", severity: EncounterSeverities.Moderate);
        await AddAlertAsync("Anticoagulant therapy", SafetyCategories.Anticoagulant, SafetySeverities.Critical);
        await AddAlertAsync("Pregnant, 20 weeks", SafetyCategories.Pregnancy, SafetySeverities.High);
        await AddAlertAsync("Needs premedication", SafetyCategories.Custom, SafetySeverities.Low, source: "Dr. Okafor's note");
        var old = AlertNamed(await AddAlertAsync("Old concern", SafetyCategories.Condition, SafetySeverities.Moderate), "Old concern");
        await AlertsAsync(s => s.ResolveAsync(old.Id, "Superseded by the new assessment", old.RowVersion, Other, default));
        await ClearancesAsync(s => s.RequestAsync(Ann, "Medical", "Cardiac clearance", "Dr. Singh", S.Actor, default));

        var c = await ContextAsync();
        Assert.Equal(new[] { "Anticoagulant therapy", "Pregnant, 20 weeks", "Penicillin", "Needs premedication" }, c.Entries.Select(e => e.Title));
        Assert.Equal(new[] { SafetySeverities.Critical, SafetySeverities.High, SafetySeverities.Moderate, SafetySeverities.Low }, c.Entries.Select(e => e.Severity!));
        var resolved = Assert.Single(c.Resolved);
        Assert.Equal(("Old concern", "Hana Hygienist", "Superseded by the new assessment"), (resolved.Title, resolved.ResolvedByName, resolved.ResolutionReason));
        Assert.Equal((3, 1, SafetySeverities.Critical, 1), (c.Summary.ActiveAlertCount, c.Summary.ActiveAllergyCount, c.Summary.HighestSeverity, c.Summary.OpenClearanceCount));
        Assert.Equal(3, c.Summary.UnacknowledgedAlertCount);
    }

    [Fact]
    public async Task Reading_the_context_changes_nothing_and_uses_the_shared_clock_for_as_of()
    {
        await AddAlertAsync();
        var audit = (await AuditAsync(nameof(SafetyAlert))).Count;
        var before = (await S.CountAsync(db => db.SafetyAlerts), await S.CountAsync(db => db.SafetyAlertVersions), await S.CountAsync(db => db.SafetyAlertAcknowledgements));
        var c = await ContextAsync();
        await ContextAsync();
        Assert.Equal(before, (await S.CountAsync(db => db.SafetyAlerts), await S.CountAsync(db => db.SafetyAlertVersions), await S.CountAsync(db => db.SafetyAlertAcknowledgements)));
        Assert.Equal(audit, (await AuditAsync(nameof(SafetyAlert))).Count);          // reading is a projection: it does not acknowledge, resolve or audit
        Assert.True(c.AsOfUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(c.Summary.AsOfUtc, c.AsOfUtc);
    }

    [Fact]
    public async Task The_summary_carries_counts_and_the_highest_severity_but_no_names()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", severity: EncounterSeverities.Severe);
        await AddAlertAsync("Prosthetic heart valve");
        var summary = await WithDb(db => Provider(db).SummaryAsync(Ann, S.Actor, default));
        var json = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain("Penicillin", json);
        Assert.DoesNotContain("heart", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((1, 1, SafetySeverities.High), (summary.ActiveAlertCount, summary.ActiveAllergyCount, summary.HighestSeverity));
    }

    // ---------- the minimal board indicator ----------

    [Fact]
    public async Task The_board_indicator_says_only_whether_there_is_an_alert_and_whether_a_clearance_is_open()
    {
        var cy = await S.PatientAsync("Cy", "Poe");
        await AddAlertAsync("HIV positive", SafetyCategories.Condition, SafetySeverities.Critical, "Viral load undetectable");
        await ClearancesAsync(s => s.RequestAsync(Bo, "Medical", "Cardiac clearance", null, S.Actor, default));
        var map = await WithDb(db => new SafetyContextService(db, Clock).IndicatorsAsync([Ann, Bo, cy], default));

        Assert.Equal(new SafetyIndicator(true, false), map[Ann]);
        Assert.Equal(new SafetyIndicator(false, true), map[Bo]);
        Assert.False(map.ContainsKey(cy));                                // nothing to show: no indicator, and certainly no invented one

        var json = JsonSerializer.Serialize(map[Ann], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("""{"alert":true,"clearance":false}""", json);       // two booleans: no category, name, severity or count can leak through
        Assert.DoesNotContain("HIV", JsonSerializer.Serialize(map));
    }

    [Fact]
    public async Task A_severe_allergy_raises_the_indicator_but_a_milder_one_a_resolved_alert_and_a_closed_clearance_do_not()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Latex", severity: EncounterSeverities.Moderate, patient: Bo);
        var a = AlertNamed(await AddAlertAsync("Old", patient: Bo), "Old");
        await AlertsAsync(s => s.ResolveAsync(a.Id, "Gone", a.RowVersion, S.Actor, default));
        var asked = ClearanceOf(await ClearancesAsync(s => s.RequestAsync(Bo, "Dental", "Specialist opinion", null, S.Actor, default)), "Dental");
        await ClearancesAsync(s => s.CancelAsync(asked.Id, "Not needed", asked.RowVersion, S.Actor, default));
        Assert.Empty(await WithDb(db => new SafetyContextService(db, Clock).IndicatorsAsync([Bo], default)));

        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", severity: EncounterSeverities.Severe, patient: Bo);
        Assert.Equal(new SafetyIndicator(true, false), (await WithDb(db => new SafetyContextService(db, Clock).IndicatorsAsync([Bo], default)))[Bo]);
        Assert.Empty(await WithDb(db => new SafetyContextService(db, Clock).IndicatorsAsync([], default)));
    }

    [Fact]
    public async Task Indicators_for_a_whole_board_are_read_in_one_pass_not_one_query_per_patient()
    {
        var patients = new List<Guid> { Ann, Bo };
        for (var i = 0; i < 8; i++) patients.Add(await S.PatientAsync($"P{i}", "Test"));
        await AddAlertAsync(patient: patients[5]);
        var queries = new System.Collections.Concurrent.ConcurrentBag<string>();
        var options = new DbContextOptionsBuilder<Alveara.Api.Data.AlveraDbContext>().UseSqlServer(Fixture.ConnectionString).AddInterceptors(new CountingInterceptor(queries)).Options;
        await using var db = new Alveara.Api.Data.AlveraDbContext(options);
        var map = await new SafetyContextService(db, Clock).IndicatorsAsync(patients, default);
        Assert.True(map[patients[5]].Alert);
        Assert.True(queries.Count <= 3, $"{queries.Count} queries for {patients.Count} patients");
    }

    private sealed class CountingInterceptor(System.Collections.Concurrent.ConcurrentBag<string> queries) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            queries.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
