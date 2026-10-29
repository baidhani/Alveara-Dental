using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>
/// Turns a report into CSV without re-computing anything: the file is made from the SAME report object the screen shows, so a total in the file is the total on the screen. Layout: a block of lines saying exactly what
/// the report covers (report, definition version, period, time zone, how many provider filters, when it was made, how it was read), then the report's own numbers as a Summary of name/value lines, then one table for
/// each list in the report. Rules that make the file safe to open in a spreadsheet: values are quoted when they hold a comma, quote or line break; a text that begins with = + - @ or a tab/return is prefixed with an
/// apostrophe so a spreadsheet cannot run it as a formula (a number is never changed, so a negative amount stays a number); lines end with CRLF; the file starts with a byte-order mark so Excel reads it as UTF-8.
/// It holds ids and numbers only, exactly what the report holds, never a name the report does not hold.
/// </summary>
public static class ReportCsv
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>The CSV text for a report and the number of data lines in it (the summary lines plus the table rows), which the caller checks against its limit.</summary>
    public static (string Text, int DataLines) Build<T>(ReportResult<T> report)
    {
        var lines = new List<string[]>
        {
            new[] { "Report", report.ReportId },
            new[] { "Definition version", report.DefinitionVersion.ToString(CultureInfo.InvariantCulture) },
            new[] { "From", report.Filter.From },
            new[] { "To", report.Filter.To },
            new[] { "Days", report.Filter.Days.ToString(CultureInfo.InvariantCulture) },
            new[] { "Time zone", report.Filter.TimeZone },
            new[] { "Provider filters", report.Filter.ProviderIds.Count.ToString(CultureInfo.InvariantCulture) },
            new[] { "Location filter", report.Filter.LocationId is null ? "No" : "Yes" },
            new[] { "Made at (UTC)", report.GeneratedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) },
            new[] { "Read", report.ReadConsistency == ReportReadModes.Snapshot ? "All numbers read at one moment" : "Read one table after another" },
            new[] { "Records", report.RecordCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "Nothing matched", report.NoData ? "Yes" : "No" },
        };
        var dataLines = 0;
        var node = JsonSerializer.SerializeToNode(report.Data, Json);
        if (node is JsonObject root)
        {
            var summary = new List<string[]>();
            var tables = new List<(string Title, JsonArray Rows)>();
            Walk(root, "", summary, tables);
            if (summary.Count > 0)
            {
                lines.Add([]); lines.Add(["Summary"]); lines.Add(["Name", "Value"]);
                lines.AddRange(summary);
                dataLines += summary.Count;
            }
            foreach (var (title, rows) in tables)
            {
                lines.Add([]); lines.Add([title]);
                if (rows.Count == 0) { lines.Add(["(no rows)"]); continue; }
                var flat = rows.Select(r => Flatten(r)).ToList();
                var columns = flat.SelectMany(f => f.Keys).Distinct().ToList();
                lines.Add(columns.Select(Humanize).ToArray());
                foreach (var row in flat) lines.Add(columns.Select(c => row.TryGetValue(c, out var v) ? v : "").ToArray());
                dataLines += flat.Count;
            }
        }
        var text = new StringBuilder("﻿");
        foreach (var line in lines) text.Append(string.Join(',', line.Select(Cell))).Append("\r\n");
        return (text.ToString(), dataLines);
    }

    /// <summary>Scalars become Summary lines (a nested object's names are joined with its parent's); every array becomes a table titled by its name.</summary>
    private static void Walk(JsonObject obj, string prefix, List<string[]> summary, List<(string, JsonArray)> tables)
    {
        foreach (var (key, value) in obj)
        {
            var name = prefix.Length == 0 ? Humanize(key) : $"{prefix} - {Humanize(key)}";
            switch (value)
            {
                case JsonArray array: tables.Add((name, array)); break;
                case JsonObject child: Walk(child, name, summary, tables); break;
                default: summary.Add([name, Text(value)]); break;
            }
        }
    }

    /// <summary>One table row as column name to text. A nested object's columns are joined with its parent's; a list inside a row is kept as JSON text rather than lost.</summary>
    private static Dictionary<string, string> Flatten(JsonNode? row)
    {
        var result = new Dictionary<string, string>();
        if (row is JsonObject obj) Add(obj, "", result);
        else result["value"] = Text(row);
        return result;
    }

    private static void Add(JsonObject obj, string prefix, Dictionary<string, string> into)
    {
        foreach (var (key, value) in obj)
        {
            var name = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (value is JsonObject child) Add(child, name, into);
            else into[name] = value is JsonArray array ? array.ToJsonString(Json) : Text(value);
        }
    }

    /// <summary>The text of a value. A string that begins like a formula is neutralised; a number or a boolean is written plainly.</summary>
    private static string Text(JsonNode? value)
    {
        if (value is null) return "";
        if (value is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return Neutralise(s);
            if (v.TryGetValue<bool>(out var b)) return b ? "Yes" : "No";
            return v.ToJsonString();
        }
        return value.ToJsonString(Json);
    }

    public static string Neutralise(string s) => s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + s : s;

    private static string Cell(string s) => s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    /// <summary>"netProduction" becomes "Net production", "ledger.linesTotal" becomes "Ledger lines total".</summary>
    public static string Humanize(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name)
        {
            if (c == '.') { text.Append(' '); continue; }
            if (char.IsUpper(c) && text.Length > 0 && text[^1] != ' ') text.Append(' ');
            text.Append(text.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return text.ToString();
    }
}
