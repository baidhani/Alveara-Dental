using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using static Alveara.Api.Tests.SchedulingApiHarness;

namespace Alveara.Api.Tests;

/// <summary>
/// The year of data the scale tests read, made ONCE for the whole class (none of the tests changes it): 4,380 appointments (one provider and operatory, twelve half-hour appointments a day from 14:00 UTC for every
/// day of 2030; every tenth is cancelled and every twenty-fifth is a no-show) and 5,000 ledger entries over 20 accounts (each has a 5,000 adjustment and then 249 payments of 10, so no account ever owes less than nothing,
/// spread over 300 days of 2030).
/// </summary>
public sealed class ReportScaleFixture : IAsyncLifetime
{
    public const int SlotsPerDay = 12, Days = 365, Accounts = 20, PaymentsPerAccount = 249;
    public readonly TestDatabaseFixture Database = new();
    internal readonly CapturingLoggerProvider Logs = new();
    public SchedulingApiHarness Api = null!;
    public SchedulingTestSupport Support = null!;
    public Session Admin = null!;
    public int Cancelled, NoShow;

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        Api = new SchedulingApiHarness(Database)
        {
            ConfigureLogging = b =>
            {
                b.AddProvider(Logs);
                b.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
            },
        };
        try
        {
            Support = new SchedulingTestSupport(Database);
            await Support.ArrangeAsync();
            Admin = await Api.SessionAsync("Admin");
            await SeedAppointmentsAsync();
            await SeedLedgerAsync();
        }
        catch
        {
            await DisposeAsync();                                               // xUnit does not dispose a fixture whose setup threw: without this a failed seed leaves its test database behind
            throw;
        }
    }

    public Task DisposeAsync() { Api.Dispose(); return Database.DisposeAsync(); }

    private async Task SeedAppointmentsAsync()
    {
        var patient = await Support.PatientAsync();
        await using var db = Database.CreateContext();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var first = new DateTimeOffset(2030, 1, 1, 14, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < Days * SlotsPerDay; i++)
        {
            var start = first.AddDays(i / SlotsPerDay).AddMinutes(30 * (i % SlotsPerDay));
            var status = i % 25 == 0 ? AppointmentStatuses.NoShow : i % 10 == 0 ? AppointmentStatuses.Cancelled : AppointmentStatuses.Scheduled;
            if (status == AppointmentStatuses.NoShow) NoShow++;
            if (status == AppointmentStatuses.Cancelled) Cancelled++;
            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(), PatientId = patient, ProviderProfileId = Support.ProviderA, OperatoryId = Support.Op1, AppointmentTypeId = Support.Short30, StartUtc = start, EndUtc = start.AddMinutes(30), DurationMinutes = 30,
                Status = status, CancelReason = status == AppointmentStatuses.Cancelled ? "Patient asked" : null, StatusChangedAtUtc = status == AppointmentStatuses.Scheduled ? null : start.AddDays(-3),
                StatusChangedByUserId = status == AppointmentStatuses.Scheduled ? null : Support.Actor,
            });
            if (i % 1000 == 999) { await db.SaveChangesAsync(); db.ChangeTracker.Clear(); }
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedLedgerAsync()
    {
        await using var db = Database.CreateContext();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var first = new DateTimeOffset(2030, 1, 2, 15, 0, 0, TimeSpan.Zero);
        for (var a = 0; a < Accounts; a++)
        {
            var patient = await Support.PatientAsync($"Scale{a}", "Account");
            var account = new BillingAccount { Id = Guid.NewGuid(), PatientId = patient, CreatedAtUtc = first, CreatedByUserId = Support.Actor };
            db.BillingAccounts.Add(account);
            db.BillingEntries.Add(new BillingEntry { Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patient, EntryNumber = 1, Kind = BillingKinds.Adjustment, Amount = 5000m, Reason = "Scale test balance", IdempotencyKey = $"scale-{a}-1", RecordedAtUtc = first, RecordedByUserId = Support.Actor });
            await db.SaveChangesAsync();                                        // the opening adjustment first: the database refuses a payment that would leave the account below nothing
            for (var n = 2; n <= PaymentsPerAccount + 1; n++)
                db.BillingEntries.Add(new BillingEntry
                {
                    Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patient, EntryNumber = n, Kind = BillingKinds.Payment, Amount = -10m, PaymentMethod = n % 2 == 0 ? "Cash" : "Card", IdempotencyKey = $"scale-{a}-{n}",
                    RecordedAtUtc = first.AddDays(n % 300).AddMinutes(n), RecordedByUserId = Support.Actor,
                });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }
}

/// <summary>
/// ALV-N006 performance smoke test: the reports over a full year of a busy practice (see <see cref="ReportScaleFixture"/>), through the real API. What is checked on EVERY run is shape, not speed: each report still
/// answers, its numbers still reconcile with the data that was put in, the number of SQL commands a report sends stays small (a report that starts asking the database once per row is caught here), a range over the limit
/// is refused with its stable code and records nothing, and a CSV of the largest report holds exactly the report's numbers. Wall-clock timings are an evidence generator like the permission-policy performance tests: they
/// run, and judge their budget (10 seconds, the worst of three), only when <c>ALVERA_PERF_EVIDENCE</c> names an output folder, so a busy full run cannot fail on machine noise; every run still fails on a report that
/// takes over a minute (a hang, not a slowdown).
/// </summary>
public class ReportScaleTests(ReportScaleFixture data) : IClassFixture<ReportScaleFixture>
{
    private const string Year = "from=2030-01-01&to=2030-12-31";
    private static readonly string[] AllReports = ["schedule", "patient-flow", "cancellations", "follow-ups", "production", "collections", "balances", "treatment-plans", "completed-procedures", "provider-activity", "measurement/metrics"];

    private async Task<JsonElement> GetAsync(string url)
    {
        var response = await data.Admin.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private int Commands() => data.Logs.Entries.Count(e => e.Category.Contains("Database.Command"));

    [Fact]
    public async Task A_full_year_reconciles_with_the_data_that_was_put_in()
    {
        var schedule = (await GetAsync($"/api/reports/schedule?{Year}")).GetProperty("data").GetProperty("totals");
        Assert.Equal(ReportScaleFixture.Days * ReportScaleFixture.SlotsPerDay, schedule.GetProperty("appointments").GetInt32());
        Assert.Equal(data.Cancelled, schedule.GetProperty("cancelled").GetInt32());
        Assert.Equal(data.NoShow, schedule.GetProperty("noShow").GetInt32());

        var cancellations = (await GetAsync($"/api/reports/cancellations?{Year}")).GetProperty("data");
        Assert.True(cancellations.GetProperty("reconciled").GetBoolean());
        Assert.Equal(data.Cancelled, cancellations.GetProperty("totals").GetProperty("cancelled").GetInt32());

        var collections = (await GetAsync($"/api/reports/collections?{Year}")).GetProperty("data");
        Assert.Equal(ReportScaleFixture.Accounts * ReportScaleFixture.PaymentsPerAccount, collections.GetProperty("paymentEntries").GetInt32());
        Assert.Equal(ReportScaleFixture.Accounts * ReportScaleFixture.PaymentsPerAccount * 10m, collections.GetProperty("paymentsReceived").GetDecimal());
        Assert.True(collections.GetProperty("ledger").GetProperty("reconciled").GetBoolean());

        var balances = (await GetAsync($"/api/reports/balances?{Year}")).GetProperty("data");
        Assert.True(balances.GetProperty("reconciled").GetBoolean());
        Assert.Equal(ReportScaleFixture.Accounts * (5000m - ReportScaleFixture.PaymentsPerAccount * 10m), balances.GetProperty("atEnd").GetProperty("netBalance").GetDecimal());
    }

    [Fact]
    public async Task Every_report_over_a_year_answers_with_a_small_fixed_number_of_database_commands()
    {
        foreach (var report in AllReports)
        {
            data.Logs.Entries.Clear();
            await GetAsync($"/api/reports/{report}?{Year}");
            Assert.InRange(Commands(), 1, 60);                                  // not one command per row: 4,380 appointments and 5,000 entries must not become thousands of commands
        }
    }

    [Fact]
    public async Task A_range_over_the_limit_is_refused_by_its_code_and_records_nothing()
    {
        await using var before = data.Database.CreateContext();
        var viewed = await before.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed);

        var response = await data.Admin.Client.GetAsync("/api/reports/schedule?from=2030-01-01&to=2031-06-30");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("range_too_large", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
        await using var after = data.Database.CreateContext();
        Assert.Equal(viewed, await after.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }

    [Fact]
    public async Task The_csv_of_a_year_holds_exactly_the_numbers_of_the_report()
    {
        var response = await data.Admin.Client.GetAsync($"/api/reports/schedule/export?{Year}");
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Totals - Appointments,{ReportScaleFixture.Days * ReportScaleFixture.SlotsPerDay}\r\n", csv);
        Assert.Contains($"Totals - Cancelled,{data.Cancelled}\r\n", csv);
        var collections = await (await data.Admin.Client.GetAsync($"/api/reports/collections/export?{Year}")).Content.ReadAsStringAsync();
        var received = System.Text.RegularExpressions.Regex.Match(collections, @"Payments received,(-?[0-9.]+)\r\n");        // money keeps its two decimals (49800.00), so the value is compared, not the text
        Assert.True(received.Success);
        Assert.Equal(ReportScaleFixture.Accounts * ReportScaleFixture.PaymentsPerAccount * 10m, decimal.Parse(received.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------- timings (evidence generator) ----------

    private static string? EvidenceFolder => Environment.GetEnvironmentVariable("ALVERA_PERF_EVIDENCE") is { Length: > 0 } f ? f : null;

    [Fact]
    public async Task No_report_over_a_year_takes_a_minute_and_with_evidence_requested_each_stays_inside_ten_seconds()
    {
        var worst = new Dictionary<string, double>();
        foreach (var report in AllReports.Append("schedule/export").Append("collections/export"))
        {
            var seconds = new List<double>();
            for (var repeat = 0; repeat < (EvidenceFolder is null ? 1 : 3); repeat++)
            {
                var watch = Stopwatch.StartNew();
                var response = await data.Admin.Client.GetAsync($"/api/reports/{report}?{Year}");
                watch.Stop();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                seconds.Add(watch.Elapsed.TotalSeconds);
            }
            worst[report] = Math.Round(seconds.Max(), 3);
            Assert.True(worst[report] < 60, $"{report} took {worst[report]} s");                // every run: a hang fails
        }

        if (EvidenceFolder is not { } folder) return;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "report-scale-timings.json"), JsonSerializer.Serialize(new { appointments = ReportScaleFixture.Days * ReportScaleFixture.SlotsPerDay, ledgerEntries = ReportScaleFixture.Accounts * (ReportScaleFixture.PaymentsPerAccount + 1), budgetSeconds = 10, worstOfThreeSeconds = worst }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var (report, seconds) in worst) Assert.True(seconds < 10, $"{report} took {seconds} s (budget 10 s)");
    }
}
