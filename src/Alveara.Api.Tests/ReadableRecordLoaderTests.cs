using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Portability.Package;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, the readable record against a real SQL Server, built from a real chart that crosses the domains: it is a function of its own (no package, no history row, no log entry), reads the patient's own rows and
/// no one else's, spells names and codes out for a person (a provider's display name, a procedure's code and description, a document's category), shows billing as totals by kind and never as a balance, lists documents with
/// their hash and says the files are not in it, carries no account or secret, and refuses an unknown patient.
/// </summary>
public class ReadableRecordLoaderTests : MergeChartTestBase
{
    private async Task<ReadableRecord> LoadAsync(Guid patient) => await WithDb(db => new ReadableRecordLoader(db, Clock).LoadAsync(patient, default));

    private static ReadableSection Section(ReadableRecord record, string key) => record.Sections.Single(s => s.Key == key);

    [Fact]
    public async Task A_real_chart_gives_every_section_with_rows_that_agree_with_the_database()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var counts = await CountsAsync(Ann);

        var record = await LoadAsync(Ann);

        Assert.Equal(new[] { "medical_history", "safety_alerts", "clearances", "dental_chart", "diagnoses", "treatment_plans", "completed_procedures", "visits", "prescriptions", "appointments", "billing", "documents", "periodontal", "forms", "follow_ups", "referrals_and_labs", "recall" },
            record.Sections.Select(s => s.Key).ToArray());
        Assert.Equal(counts["ClinicalRecordItems.PatientId"], Section(record, "medical_history").Rows.Count);
        Assert.Equal(counts["SafetyAlerts.PatientId"], Section(record, "safety_alerts").Rows.Count);
        Assert.Equal(counts["ToothFindings.PatientId"], Section(record, "dental_chart").Rows.Count);
        Assert.Equal(counts["Diagnoses.PatientId"], Section(record, "diagnoses").Rows.Count);
        Assert.Equal(counts["TreatmentPlanItems.PatientId"], Section(record, "treatment_plans").Rows.Count);
        Assert.Equal(counts["CompletedProcedures.PatientId"], Section(record, "completed_procedures").Rows.Count);
        Assert.Equal(counts["Appointments.PatientId"], Section(record, "appointments").Rows.Count);
        Assert.Equal(counts["BillingEntries.PatientId"], Section(record, "billing").Rows.Count);
        Assert.Equal(counts["Prescriptions.PatientId"], Section(record, "prescriptions").Rows.Count);
        Assert.Equal(counts["Documents.PatientId"], Section(record, "documents").Rows.Count);
        Assert.All(new[] { "medical_history", "safety_alerts", "dental_chart", "diagnoses", "treatment_plans", "completed_procedures", "appointments", "billing", "prescriptions", "documents" }, key => Assert.NotEmpty(Section(record, key).Rows));
        Assert.All(record.Sections, s => Assert.All(s.Rows, r => Assert.Equal(s.Headers.Count, r.Count)));      // every row fits its headings
    }

    [Fact]
    public async Task The_summary_names_the_patient_and_only_this_patients_rows_are_read()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var ann = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann));

        var record = await LoadAsync(Ann);

        Assert.Equal(Ann, record.PatientId);
        Assert.Equal($"{ann.FirstName} {ann.LastName}", record.PatientName);
        Assert.Equal(ann.DateOfBirth.ToString("yyyy-MM-dd"), record.Summary.Single(f => f.Label == "Date of birth").Value);
        Assert.Equal("Active", record.Summary.Single(f => f.Label == "Record status").Value);
        var others = (await CountsAsync(Bo))["Appointments.PatientId"];
        Assert.True(others > 0, "the other patient has appointments of their own");
        Assert.Equal((await CountsAsync(Ann))["Appointments.PatientId"], Section(record, "appointments").Rows.Count);   // not Ann's plus Bo's
    }

    [Fact]
    public async Task Names_and_codes_are_spelled_out_for_a_person()
    {
        await GiveRetiringChartAsync();

        var record = await LoadAsync(Ann);

        var appointment = Section(record, "appointments").Rows.First();
        Assert.All(new[] { 2, 3, 4 }, column => Assert.False(string.IsNullOrWhiteSpace(appointment[column]), $"appointments column {column} (type, provider, room) is spelled out"));
        Assert.DoesNotContain(Section(record, "appointments").Rows.SelectMany(r => r), cell => Guid.TryParse(cell, out _));                       // no raw ids
        var item = Section(record, "treatment_plans").Rows.First();
        Assert.Matches(@"^\S+ \S+", item[3]);                                                                    // a procedure code and its description
        Assert.All(Section(record, "completed_procedures").Rows, r => Assert.Contains(r[2], Section(record, "treatment_plans").Rows.Select(p => $"{p[0].Split(" (")[0]} #{p[2]}")));
    }

    [Fact]
    public async Task Times_are_written_in_the_practice_time_zone_with_their_offset_not_in_UTC()
    {
        await GiveRetiringChartAsync();
        var starts = await WithDb(db => db.Appointments.AsNoTracking().Where(a => a.PatientId == Ann).OrderBy(a => a.StartUtc).Select(a => a.StartUtc).ToListAsync());

        var rows = Section(await LoadAsync(Ann), "appointments").Rows;

        Assert.Equal(starts.Select(s => PracticeTimeText.Moment(Clock, s)), rows.Select(r => r[0]));
        Assert.All(rows, r => Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2} \(UTC[+-]\d{2}:\d{2}\)$", r[0]));       // local time with its offset, never "UTC" as the zone
        Assert.DoesNotContain(rows, r => r[0].EndsWith(" UTC"));
    }

    [Fact]
    public async Task Billing_is_shown_as_totals_by_kind_and_never_as_a_balance()
    {
        await GiveRetiringChartAsync();

        var billing = Section(await LoadAsync(Ann), "billing");

        Assert.NotNull(billing.Note);
        Assert.Contains("not a balance", billing.Note);
        Assert.Contains("Charge:", billing.Note);
    }

    [Fact]
    public async Task Documents_are_listed_with_their_hash_and_the_section_says_the_files_are_not_in_it()
    {
        await GiveRetiringChartAsync();
        var stored = await WithDb(db => db.Documents.AsNoTracking().Where(d => d.PatientId == Ann).Select(d => d.Sha256).ToListAsync());

        var documents = Section(await LoadAsync(Ann), "documents");

        Assert.NotEmpty(stored);
        Assert.Equal(stored.Order(), documents.Rows.Select(r => r[6]).Order());
        Assert.Contains("only in the complete export package", documents.Note);
    }

    [Fact]
    public async Task No_account_or_secret_is_in_the_record()
    {
        await GiveRetiringChartAsync();
        var usernames = await WithDb(db => db.UserAccounts.AsNoTracking().Select(u => u.Username).ToListAsync());

        var record = await LoadAsync(Ann);

        var text = string.Join("\n", record.Summary.Select(f => f.Value).Concat(record.Sections.SelectMany(s => s.Rows.SelectMany(r => r))));
        Assert.All(usernames, u => Assert.DoesNotContain(u, text));
        foreach (var secret in new[] { "PasswordHash", "MfaSecret", "SecurityStamp" }) Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Loading_the_record_writes_nothing_and_an_unknown_patient_is_refused()
    {
        await GiveRetiringChartAsync();
        var audit = await WithDb(db => db.AuditLogEntries.CountAsync());

        await LoadAsync(Ann);
        var refused = await Assert.ThrowsAsync<PatientException>(() => LoadAsync(Guid.NewGuid()));

        Assert.Equal(("patient_not_found", 404), (refused.Code, refused.StatusCode));
        Assert.Equal(audit, await WithDb(db => db.AuditLogEntries.CountAsync()));
        Assert.Equal(0, await WithDb(db => db.DataExportHistory.CountAsync()));                                  // the loader is not an export: the caller that delivers it records that
    }
}
