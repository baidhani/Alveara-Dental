using Xunit;
using Alveara.Api.Architecture.Patients.Portability.Package;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, how the readable record is shown, without a database: every value is encoded so nothing in a record can become markup; the page is self-contained (no script, no outside file, no image) and says so in a
/// content-security policy; an empty section says "None recorded" instead of vanishing; the page says plainly what it leaves out; clinical notes keep their line breaks; and the same record always gives the same page.
/// </summary>
public class ReadableRecordHtmlTests
{
    private static readonly Guid ExportId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly ReadableRecordContext Package = new(ExportId, "2026-10-26 09:05 (UTC-05:00)", "America/Chicago", InsidePackage: true);
    private static readonly ReadableRecordContext Alone = new(ExportId, "2026-10-26 09:05 (UTC-05:00)", "America/Chicago", InsidePackage: false);

    private static ReadableRecord Record(params ReadableSection[] sections) =>
        new(Guid.NewGuid(), "Eli Moss", [new("Name", "Eli Moss"), new("Date of birth", "1990-05-17")], sections);

    private static ReadableSection Section(string key, string title, params string[][] rows) =>
        new(key, title, ["Name", "Details"], rows.Select(r => (IReadOnlyList<string>)r).ToList());

    [Fact]
    public void The_page_shows_the_patient_the_summary_each_section_with_its_rows_and_a_link_to_every_section()
    {
        var html = ReadableRecordHtml.Render(Record(Section("allergies", "Allergies", ["Penicillin", "Hives"]), Section("alerts", "Alerts")), Package);

        Assert.Contains("<h1>Eli Moss</h1>", html);
        Assert.Contains("<dt>Date of birth</dt><dd>1990-05-17</dd>", html);
        Assert.Contains("<h2 id=\"s-allergies\">Allergies</h2>", html);
        Assert.Contains("<td>Penicillin</td><td>Hives</td>", html);
        Assert.Contains("<a href=\"#s-allergies\">Allergies (1)</a>", html);
        Assert.Contains("<a href=\"#s-alerts\">Alerts (0)</a>", html);
        Assert.Contains("lang=\"en\"", html);
        Assert.Contains("<th scope=\"col\">Name</th>", html);
    }

    [Fact]
    public void A_section_with_no_rows_says_none_recorded_instead_of_disappearing()
    {
        var html = ReadableRecordHtml.Render(Record(Section("alerts", "Safety alerts")), Package);

        Assert.Contains("<h2 id=\"s-alerts\">Safety alerts</h2>", html);
        Assert.Contains("<p class=\"none\">None recorded.</p>", html);
        Assert.DoesNotContain("<table>", html);
    }

    [Fact]
    public void Nothing_in_a_record_can_become_markup()
    {
        const string attack = "<script>alert('x')</script><img src=\"http://evil.invalid/p.png\" onerror=\"x()\">";
        var record = new ReadableRecord(Guid.NewGuid(), attack, [new("Name", attack)], [Section("notes", attack, [attack, "a & b"])]);

        var html = ReadableRecordHtml.Render(record, Package);

        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("<img src", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;", html);
        Assert.Contains("a &amp; b", html);
    }

    [Fact]
    public void The_page_is_self_contained_and_forbids_everything_it_does_not_use()
    {
        var html = ReadableRecordHtml.Render(Record(Section("allergies", "Allergies", ["Penicillin", "Hives"])), Package);

        Assert.Contains("Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\"", html);
        foreach (var forbidden in new[] { "<script", "<link", "<img", "<iframe", "<object", "<form", "src=", "http://", "https://", "@import", "url(" })
            Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_page_says_what_it_is_and_what_it_leaves_out_depending_on_where_it_is_found()
    {
        var inside = ReadableRecordHtml.Render(Record(), Package);
        var alone = ReadableRecordHtml.Render(Record(), Alone);

        Assert.Contains("personal health information", inside);
        Assert.Contains("2026-10-26 09:05 (UTC-05:00)", inside);
        Assert.Contains("All times are in the practice time zone (America/Chicago).", inside);
        Assert.Contains(ExportId.ToString(), inside);
        Assert.Contains("package's <em>documents</em> folder", inside);
        Assert.Contains("complete export package, not here", alone);
        Assert.DoesNotContain("package's <em>documents</em> folder", alone);
    }

    [Fact]
    public void A_package_made_without_the_document_files_says_so_instead_of_pointing_at_a_folder_that_is_not_there()
    {
        var without = ReadableRecordHtml.Render(Record(), Package with { DocumentFilesIncluded = false });

        Assert.Contains("made without the document files", without);
        Assert.DoesNotContain("<em>documents</em> folder", without);
        Assert.Contains("in this patient's own folder", ReadableRecordHtml.Render(Record(), Package));
    }

    [Fact]
    public void Line_breaks_in_a_note_are_kept_and_the_same_record_always_gives_the_same_page()
    {
        var record = Record(Section("visits", "Visits", ["2026-10-01", "Subjective: sore\r\nObjective: swelling\nPlan: review"]));

        var first = ReadableRecordHtml.Render(record, Package);

        Assert.Contains("Subjective: sore<br>Objective: swelling<br>Plan: review", first);
        Assert.Equal(first, ReadableRecordHtml.Render(record, Package));
    }
}
