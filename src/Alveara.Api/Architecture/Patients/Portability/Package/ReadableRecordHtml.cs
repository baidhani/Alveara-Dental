using System.Net;
using System.Text;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: shows a <see cref="ReadableRecord"/> as one self-contained web page that opens in any browser and prints cleanly: no script, no outside file, no image, a content-security policy that forbids all of them,
/// and every value from the record encoded so nothing in a record can become markup. It states at the top that it is a copy of personal health information and what it leaves out (the document files), and every
/// section with no rows says "None recorded" instead of disappearing, so an empty section is never mistaken for a missing one. Pure and deterministic: the same record gives the same page.
/// </summary>
public static class ReadableRecordHtml
{
    public static string Render(ReadableRecord record, ReadableRecordContext context)
    {
        var html = new StringBuilder(16 * 1024);
        html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(E($"Patient record: {record.PatientName}")).Append("</title>\n<style>").Append(Style).Append("</style>\n</head>\n<body>\n");
        html.Append("<header>\n<h1>").Append(E(record.PatientName)).Append("</h1>\n");
        html.Append("<p class=\"meta\">Patient record copy. Made ").Append(E(context.GeneratedAt)).Append(", export ").Append(E(context.ExportId.ToString())).Append(". All times are in the practice time zone (").Append(E(context.TimeZoneId)).Append(").</p>\n");
        html.Append("<p class=\"notice\">This page holds personal health information. Keep it where only people allowed to see it can reach it, and delete it when it is no longer needed. ");
        html.Append(context.InsidePackage
            ? "Document files (scans, images, letters) are listed here and are included in the package's <em>documents</em> folder when the export was made with files."
            : "This page lists documents by name only. The document files, and the full data behind this page, are in the complete export package, not here.");
        html.Append("</p>\n</header>\n<main>\n");

        html.Append("<section aria-labelledby=\"s-summary\">\n<h2 id=\"s-summary\">Patient</h2>\n<dl>\n");
        foreach (var fact in record.Summary) html.Append("<dt>").Append(E(fact.Label)).Append("</dt><dd>").Append(Cell(fact.Value)).Append("</dd>\n");
        html.Append("</dl>\n</section>\n");

        html.Append("<nav aria-label=\"Sections\"><ul>");
        foreach (var section in record.Sections) html.Append("<li><a href=\"#s-").Append(E(section.Key)).Append("\">").Append(E(section.Title)).Append(" (").Append(section.Rows.Count).Append(")</a></li>");
        html.Append("</ul></nav>\n");

        foreach (var section in record.Sections) AppendSection(html, section);
        html.Append("</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static void AppendSection(StringBuilder html, ReadableSection section)
    {
        html.Append("<section aria-labelledby=\"s-").Append(E(section.Key)).Append("\">\n<h2 id=\"s-").Append(E(section.Key)).Append("\">").Append(E(section.Title)).Append("</h2>\n");
        if (section.Note is not null) html.Append("<p class=\"note\">").Append(E(section.Note)).Append("</p>\n");
        if (section.Rows.Count == 0)
        {
            html.Append("<p class=\"none\">None recorded.</p>\n</section>\n");
            return;
        }
        html.Append("<table>\n<caption class=\"sr\">").Append(E(section.Title)).Append("</caption>\n<thead><tr>");
        foreach (var header in section.Headers) html.Append("<th scope=\"col\">").Append(E(header)).Append("</th>");
        html.Append("</tr></thead>\n<tbody>\n");
        foreach (var row in section.Rows)
        {
            html.Append("<tr>");
            foreach (var cell in row) html.Append("<td>").Append(Cell(cell)).Append("</td>");
            html.Append("</tr>\n");
        }
        html.Append("</tbody>\n</table>\n</section>\n");
    }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    /// <summary>A value shown in a cell: encoded first, then line breaks kept as breaks (clinical notes are several lines).</summary>
    private static string Cell(string? text) => E(text).Replace("\r\n", "\n").Replace("\n", "<br>");

    private const string Style = """
        body{font:15px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;color:#1b1f23;background:#fff;margin:0;padding:24px;max-width:1100px;margin-inline:auto}
        h1{font-size:1.7rem;margin:0 0 4px}h2{font-size:1.15rem;margin:28px 0 8px;border-bottom:2px solid #1f4e79;padding-bottom:4px;color:#1f4e79}
        .meta{color:#4a5560;margin:0 0 8px}.notice{background:#fff7e0;border:1px solid #d9b44a;padding:10px 12px;border-radius:4px}
        .note{color:#4a5560;margin:4px 0}.none{color:#4a5560;font-style:italic}
        dl{display:grid;grid-template-columns:max-content 1fr;gap:4px 16px}dt{font-weight:600}dd{margin:0}
        nav ul{display:flex;flex-wrap:wrap;gap:6px 16px;list-style:none;padding:0;margin:20px 0}a{color:#1f4e79}
        table{border-collapse:collapse;width:100%;margin:6px 0}th,td{border:1px solid #c7ced6;padding:5px 8px;text-align:left;vertical-align:top}th{background:#eef2f6}
        .sr{position:absolute;left:-9999px}
        @media print{body{padding:0}nav{display:none}h2{break-after:avoid}tr{break-inside:avoid}a{color:inherit;text-decoration:none}}
        """;
}
