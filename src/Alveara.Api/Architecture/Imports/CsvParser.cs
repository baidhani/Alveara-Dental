using System.Text;

namespace Alveara.Api.Architecture.Imports;

/// <summary>One thing wrong with a file. <paramref name="Code"/> is stable (the screens map it to words); <paramref name="Row"/> counts records with the header as row 1; <paramref name="Message"/> never repeats a value from the file.</summary>
public sealed record ImportProblem(string Code, int? Row, string? Column, string Message);

/// <summary>A file that passed every structural check: the header names and the data rows, each row exactly as wide as the header.</summary>
public sealed class CsvTable(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
{
    public IReadOnlyList<string> Headers { get; } = headers;
    public IReadOnlyList<string[]> Rows { get; } = rows;
}

/// <summary>The answer for one file: the table when the file is sound, otherwise the problems (at most <see cref="ImportLimits.MaxProblemsListed"/> listed; <paramref name="TotalProblems"/> counts them all).</summary>
public sealed record CsvParseResult(CsvTable? Table, IReadOnlyList<ImportProblem> Problems, int TotalProblems);

/// <summary>
/// ALV-018-C02: reads an import file as untrusted text. It is a small parser of its own (no dependency): UTF-8 with or without a byte-order mark; comma separated; a field may be quoted, and then may hold commas, line
/// breaks and doubled quotes. A file is sound only if it is not empty, not over the limits (<see cref="ImportOptions"/>), valid UTF-8, free of control characters (a tab or a line break inside a quoted field is allowed),
/// has a header whose names are present, short enough and unique ignoring case, has at least one data row, and every row has exactly as many cells as the header. A short or long row is refused rather than padded: a shifted
/// column would put a value in the wrong field, which is exactly the silent corruption this tool exists to prevent. Any problem refuses the whole file; the problems say where (row, column name) and what, never what the value was.
/// Failure modes handled: empty, oversize, not text, control characters, unterminated or stray quotes, bad header, ragged rows, too many rows, columns or cell characters. No external call is made.
/// </summary>
public static class CsvParser
{
    public static CsvParseResult Parse(byte[] content, ImportOptions options)
    {
        var problems = new List<ImportProblem>();
        var total = 0;
        void Add(string code, int? row, string? column, string message) { total++; if (problems.Count < ImportLimits.MaxProblemsListed) problems.Add(new ImportProblem(code, row, column, message)); }
        CsvParseResult Refuse() => new(null, problems, total);

        if (content.Length == 0) { Add("file_empty", null, null, "The file is empty."); return Refuse(); }
        if (content.Length > options.MaxFileBytes) { Add("file_too_large", null, null, $"The file is larger than the {options.MaxFileBytes / (1024 * 1024)} MB limit."); return Refuse(); }

        string text;
        try { text = new UTF8Encoding(false, true).GetString(content); }
        catch (DecoderFallbackException) { Add("not_text", null, null, "The file is not valid UTF-8 text. Save it as CSV (UTF-8) and send it again."); return Refuse(); }
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        if (string.IsNullOrWhiteSpace(text)) { Add("file_empty", null, null, "The file has no content."); return Refuse(); }

        IReadOnlyList<string> headers = [];
        var rows = new List<string[]>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var quoted = false;                 // the current field began with a quote
        var afterQuote = false;             // the closing quote of the current field has been read
        var controlSeen = false;            // a control character has already been reported in this record
        var recordNumber = 1;               // the header is row 1
        var stop = false;

        string? ColumnName(int index) => index < headers.Count ? headers[index] : null;

        void EndField()
        {
            var value = field.ToString();
            if (value.Length > options.MaxCellChars) Add("cell_too_long", recordNumber, ColumnName(record.Count), $"A cell is longer than {options.MaxCellChars} characters.");
            record.Add(value);
            field.Clear(); quoted = false; afterQuote = false;
        }

        void EndRecord()
        {
            var blank = record.Count == 0 && field.Length == 0 && !quoted;
            if (blank) return;                                          // an empty line (for example after the last row) is not a record
            EndField();
            if (recordNumber == 1) CheckHeader(record);
            else if (record.Count != headers.Count) Add("row_width", recordNumber, null, $"The row has {record.Count} cell(s) but the header has {headers.Count}.");
            else rows.Add(record.ToArray());
            record = new List<string>();
            controlSeen = false;
            recordNumber++;
            if (recordNumber - 2 > options.MaxRows) { Add("too_many_rows", null, null, $"The file has more than {options.MaxRows} data rows."); stop = true; }       // records read so far, less the header, over the limit
        }

        void CheckHeader(List<string> names)
        {
            if (names.Count > options.MaxColumns) { Add("too_many_columns", 1, null, $"The file has more than {options.MaxColumns} columns."); stop = true; return; }
            var clean = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i].Trim();
                if (name.Length == 0) Add("header_empty_name", 1, null, $"Column {i + 1} has no name in the header.");
                else if (name.Length > ImportLimits.ColumnNameMax) Add("header_name_too_long", 1, null, $"Column {i + 1}'s name is longer than {ImportLimits.ColumnNameMax} characters.");
                else if (!seen.Add(name)) Add("header_duplicate", 1, name, $"Two columns are named \"{name}\" (names are compared ignoring capitals).");
                clean.Add(name);
            }

            headers = clean;
        }

        void Control(char c)
        {
            if (controlSeen) return;
            controlSeen = true;
            Add("control_character", recordNumber, null, "The row contains a control character, which is not allowed.");
        }

        for (var i = 0; i < text.Length && !stop; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { inQuotes = false; afterQuote = true; }
                }
                else
                {
                    if ((c < 0x20 && c is not ('\t' or '\n' or '\r')) || c == 0x7F) Control(c);
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case ',': EndField(); break;
                case '\r':
                case '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRecord();
                    break;
                case '"':
                    if (field.Length == 0 && !quoted) { inQuotes = true; quoted = true; }
                    else { Add("stray_quote", recordNumber, ColumnName(record.Count), "A quote appears inside a value that does not start with one."); }
                    break;
                default:
                    if (afterQuote) { Add("stray_quote", recordNumber, ColumnName(record.Count), "Text follows the closing quote of a value."); afterQuote = false; }
                    if ((c < 0x20 && c != '\t') || c == 0x7F) Control(c);
                    field.Append(c);
                    break;
            }
        }

        if (inQuotes) Add("unterminated_quote", recordNumber, ColumnName(record.Count), "A quoted value is never closed.");
        else if (!stop) EndRecord();

        if (total == 0 && rows.Count == 0) Add("no_data_rows", null, null, "The file has a header but no data rows.");
        return total > 0 ? Refuse() : new CsvParseResult(new CsvTable(headers, rows), problems, 0);
    }
}
