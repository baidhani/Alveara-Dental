using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 7, the CSV writer and the export service. The file is made from the same report object the screen shows, so its numbers ARE the screen's: a summary line and a table row carry the report's own values.
/// A value a spreadsheet could run as a formula is neutralised if it is text and left alone if it is a number (a negative amount stays a number). Quoting, line endings and the byte-order mark are exact. An export is
/// recorded; if it cannot be recorded nothing is handed over; a file over the limit is refused, not cut.
/// </summary>
public class ReportCsvTests : IAsyncLifetime
{
    private sealed record Row(string Name, decimal Amount, int Count, bool Reconciled);
    private sealed record Totals(decimal Gross, decimal Reversed);
    private sealed record Sample(decimal NetProduction, int Entries, Row[] Rows, Totals Totals);

    private readonly TestDatabaseFixture _fixture = new();
    private static readonly Guid Actor = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static ReportResult<Sample> Report(Sample data, bool noData = false, string mode = ReportReadModes.Snapshot) =>
        new("production", 1, new("2030-01-14", "2030-01-16", 3, [Guid.NewGuid()], null, "America/Chicago"), new(2030, 1, 20, 9, 30, 15, TimeSpan.Zero), mode, data.Rows.Length, noData, data);

    private static string[] Lines(string csv) => csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.None);

    private static Sample Three() => new(5m, 1, [new("a", 1m, 1, true), new("b", 1m, 1, true), new("c", 1m, 1, true)], new(5m, 0m));

    [Fact]
    public void The_file_says_what_it_covers_then_the_summary_then_one_table_per_list()
    {
        var (csv, dataLines) = ReportCsv.Build(Report(new(1234.5m, 7, [new("Exam", 100m, 2, true), new("Crown", 900.25m, 1, false)], new(1500m, -265.5m))));
        var lines = Lines(csv);

        Assert.StartsWith("﻿Report,production\r\n", csv);
        Assert.Equal(new[] { "Report,production", "Definition version,1", "From,2030-01-14", "To,2030-01-16", "Days,3", "Time zone,America/Chicago", "Provider filters,1", "Location filter,No",
            "Made at (UTC),2030-01-20T09:30:15Z", "Read,All numbers read at one moment", "Records,2", "Nothing matched,No" }, lines.Take(12));
        Assert.Equal(new[] { "", "Summary", "Name,Value", "Net production,1234.5", "Entries,7", "Totals - Gross,1500", "Totals - Reversed,-265.5", "", "Rows", "Name,Amount,Count,Reconciled", "Exam,100,2,Yes", "Crown,900.25,1,No", "" }, lines.Skip(12));
        Assert.Equal(4 + 2, dataLines);
        Assert.EndsWith("\r\n", csv);
    }

    [Fact]
    public void A_number_is_written_as_a_number_but_text_that_could_run_as_a_formula_is_neutralised()
    {
        var (csv, _) = ReportCsv.Build(Report(new(-12.5m, 0, [new("=SUM(A1)", -5m, 0, true), new("+1", 1m, 0, true), new("-cmd", 1m, 0, true), new("@x", 1m, 0, true), new("\tx", 1m, 0, true), new("safe", 1m, 0, true)], new(0m, 0m))));

        Assert.Contains("Net production,-12.5\r\n", csv);                    // a negative number is a number, never prefixed
        Assert.Contains("'=SUM(A1),-5,0,Yes", csv);
        Assert.Contains("'+1,1,0,Yes", csv);
        Assert.Contains("'-cmd,1,0,Yes", csv);
        Assert.Contains("'@x,1,0,Yes", csv);
        Assert.Contains("'\tx,1,0,Yes", csv);
        Assert.Contains("\r\nsafe,1,0,Yes\r\n", csv);
        Assert.Equal("'=A", ReportCsv.Neutralise("=A"));
        Assert.Equal("A=", ReportCsv.Neutralise("A="));
        Assert.Equal("", ReportCsv.Neutralise(""));
    }

    [Fact]
    public void A_value_with_a_comma_a_quote_or_a_line_break_is_quoted_and_a_quote_is_doubled()
    {
        var (csv, _) = ReportCsv.Build(Report(new(0m, 0, [new("a,b", 1m, 1, true), new("say \"hi\"", 1m, 1, true), new("two\nlines", 1m, 1, true)], new(0m, 0m))));

        Assert.Contains("\"a,b\",1,1,Yes", csv);
        Assert.Contains("\"say \"\"hi\"\"\",1,1,Yes", csv);
        Assert.Contains("\"two\nlines\",1,1,Yes", csv);
    }

    [Fact]
    public void An_empty_list_says_so_and_a_report_read_table_by_table_and_with_nothing_matching_says_that()
    {
        var (csv, dataLines) = ReportCsv.Build(Report(new(0m, 0, [], new(0m, 0m)), noData: true, mode: ReportReadModes.TableByTable));

        Assert.Contains("Read,Read one table after another\r\n", csv);
        Assert.Contains("Nothing matched,Yes\r\n", csv);
        Assert.Contains("\r\nRows\r\n(no rows)\r\n", csv);
        Assert.Equal(4, dataLines);                                         // the four summary lines, no rows
    }

    [Theory]
    [InlineData("netProduction", "Net production")]
    [InlineData("ledger.linesTotal", "Ledger lines total")]
    [InlineData("Name", "Name")]
    public void Names_are_made_readable(string name, string expected) => Assert.Equal(expected, ReportCsv.Humanize(name));

    [Fact]
    public async Task An_export_is_handed_over_only_after_it_is_recorded_and_the_name_says_what_it_holds()
    {
        await using var db = _fixture.CreateContext();
        var file = await Service(db).CsvAsync(Report(new(5m, 1, [new("Exam", 5m, 1, true)], new(5m, 0m))), Actor, default);

        Assert.Equal("production_2030-01-14_to_2030-01-16.csv", file.FileName);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, file.Bytes.Take(3));
        var entry = await db.AuditLogEntries.SingleAsync(e => e.EventType == AuditEventTypes.ReportExported);
        Assert.Equal(Actor, entry.PerformedByUserAccountId);
        Assert.Equal("Report production exported as CSV: 3 day(s), 5 line(s).", entry.Details);        // four summary lines and one row
        Assert.DoesNotContain("Exam", entry.Details);
    }

    [Fact]
    public async Task When_the_export_cannot_be_recorded_nothing_is_handed_over_and_a_file_over_the_limit_is_refused()
    {
        await using var failing = new SchedulingTestSupport(_fixture).FailingAuditContext();
        var ex = await Assert.ThrowsAsync<ReportException>(() => Service(failing).CsvAsync(Report(Three()), Actor, default));
        Assert.Equal(("audit_unavailable", 503), (ex.Code, ex.StatusCode));

        await using var db = _fixture.CreateContext();
        var tooBig = await Assert.ThrowsAsync<ReportException>(() => Service(db, new ReportOptions { MaxExportRows = 6 }).CsvAsync(Report(Three()), Actor, default));
        Assert.Equal(("export_too_large", 413), (tooBig.Code, tooBig.StatusCode));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportExported));       // a refused export is not recorded as one
        var exactly = await Service(db, new ReportOptions { MaxExportRows = 7 }).CsvAsync(Report(Three()), Actor, default);
        Assert.NotEmpty(exactly.Bytes);                                         // 4 summary lines + 3 rows = 7: exactly the limit still exports
    }

    private static ReportExportService Service(AlveraDbContext db, ReportOptions? options = null) =>
        new(db, Options.Create(options ?? new ReportOptions()), NullLogger<ReportExportService>.Instance);
}
