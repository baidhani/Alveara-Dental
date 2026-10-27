using System.Text;
using Xunit;
using Alveara.Api.Architecture.Imports;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02, the import file checks without a database: a sound file is read exactly (UTF-8 with or without a byte-order mark, CRLF or LF, quoted values with commas, line breaks and doubled quotes, a blank
/// last line ignored); every structural fault refuses the whole file with a stable code, a row number (the header is row 1) and a message that never repeats a value from the file: empty, only a header, not UTF-8,
/// over the size, row, column or cell limits, a control character, an unterminated or stray quote, an empty, over-long or duplicate (ignoring capitals) header name, a short or long row. The limits are tested at the
/// boundary (exactly the limit passes, one over is refused) and a hopeless file lists at most the cap of problems while counting all of them.
/// </summary>
public class CsvParserTests
{
    private static readonly ImportOptions Options = new();

    private static byte[] Utf8(string text, bool bom = false) => (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : []).Concat(Encoding.UTF8.GetBytes(text)).ToArray();

    private static CsvParseResult Parse(string text, ImportOptions? options = null, bool bom = false) => CsvParser.Parse(Utf8(text, bom), options ?? Options);

    private static string[] Codes(CsvParseResult result) => result.Problems.Select(p => p.Code).ToArray();

    [Fact]
    public void A_sound_file_is_read_exactly_with_or_without_a_byte_order_mark_and_with_either_line_ending()
    {
        foreach (var (text, bom) in new[] { ("First,Last\r\nAnn,Rivera\r\nBo,Moss\r\n", false), ("First,Last\nAnn,Rivera\nBo,Moss", true), ("First,Last\rAnn,Rivera\rBo,Moss\r\n\r\n", false) })
        {
            var result = Parse(text, bom: bom);

            Assert.NotNull(result.Table);
            Assert.Equal(["First", "Last"], result.Table!.Headers);
            Assert.Equal(2, result.Table.Rows.Count);
            Assert.Equal(["Ann", "Rivera"], result.Table.Rows[0]);
            Assert.Equal(["Bo", "Moss"], result.Table.Rows[1]);
            Assert.Empty(result.Problems);
        }
    }

    [Fact]
    public void A_quoted_value_may_hold_commas_line_breaks_tabs_and_doubled_quotes_and_an_empty_value_is_kept()
    {
        var result = Parse("Name,Note,Other\r\n\"Rivera, Ann\",\"line one\r\nline two\tend\",\r\n\"He said \"\"hi\"\"\",\"\",x\r\n");

        Assert.Equal(["Rivera, Ann", "line one\r\nline two\tend", ""], result.Table!.Rows[0]);
        Assert.Equal(["He said \"hi\"", "", "x"], result.Table.Rows[1]);
    }

    [Fact]
    public void Header_names_are_trimmed_but_values_are_kept_as_sent()
    {
        var result = Parse(" First , Last \r\n  Ann ,Rivera\r\n");

        Assert.Equal(["First", "Last"], result.Table!.Headers);
        Assert.Equal(["  Ann ", "Rivera"], result.Table.Rows[0]);                                   // the mapping step decides how to trim a value; the reader never changes one
    }

    [Fact]
    public void An_empty_file_a_blank_file_and_a_header_with_no_rows_are_refused()
    {
        Assert.Equal(["file_empty"], Codes(CsvParser.Parse([], Options)));
        Assert.Equal(["file_empty"], Codes(Parse("  \r\n \r\n")));
        Assert.Equal(["file_empty"], Codes(Parse("", bom: true)));
        Assert.Equal(["no_data_rows"], Codes(Parse("First,Last\r\n")));
        Assert.Equal(["no_data_rows"], Codes(Parse("First,Last")));
    }

    [Fact]
    public void A_file_that_is_not_valid_utf8_is_refused_and_so_is_one_over_the_size_limit()
    {
        Assert.Equal(["not_text"], Codes(CsvParser.Parse([0x41, 0xC3, 0x28, 0x0A, 0x42], Options)));

        var small = new ImportOptions { MaxFileBytes = 20 };
        Assert.NotNull(Parse("A\r\n12345678901\r\n", small).Table);                              // 16 bytes, under the 20-byte limit
        Assert.Equal(["file_too_large"], Codes(CsvParser.Parse(new byte[21], small)));             // the size is checked before anything is decoded
        Assert.Null(CsvParser.Parse(Utf8(new string('x', 21)), small).Table);
    }

    [Fact]
    public void The_row_limit_is_exact_and_the_column_and_cell_limits_are_exact()
    {
        var rows = new ImportOptions { MaxRows = 3 };
        Assert.NotNull(Parse("A\r\n1\r\n2\r\n3\r\n", rows).Table);
        Assert.Equal(["too_many_rows"], Codes(Parse("A\r\n1\r\n2\r\n3\r\n4\r\n", rows)));

        var columns = new ImportOptions { MaxColumns = 3 };
        Assert.NotNull(Parse("A,B,C\r\n1,2,3\r\n", columns).Table);
        Assert.Equal(["too_many_columns"], Codes(Parse("A,B,C,D\r\n1,2,3,4\r\n", columns)));

        var cells = new ImportOptions { MaxCellChars = 5 };
        Assert.NotNull(Parse("A\r\n12345\r\n", cells).Table);
        var over = Parse("Notes\r\n123456\r\n", cells);
        Assert.Equal([("cell_too_long", 2, "Notes")], over.Problems.Select(p => (p.Code, p.Row ?? 0, p.Column ?? "")).ToArray());
    }

    [Fact]
    public void A_control_character_is_refused_once_for_each_row_but_a_tab_is_allowed()
    {
        Assert.NotNull(Parse("A,B\r\nx\ty,z\r\n").Table);

        var result = Parse("A,B\r\nx\0y,z\u0001\r\nfine,fine\r\nq,\u007F\r\n");

        Assert.Equal([("control_character", 2), ("control_character", 4)], result.Problems.Select(p => (p.Code, p.Row ?? 0)).ToArray());
        Assert.Null(result.Table);
    }

    [Fact]
    public void A_quote_that_is_never_closed_or_that_appears_where_it_does_not_belong_is_refused()
    {
        Assert.Equal(["unterminated_quote"], Codes(Parse("A,B\r\n\"never closed,2\r\n3,4\r\n")));
        Assert.Contains("stray_quote", Codes(Parse("A,B\r\nab\"c,2\r\n")));                          // a quote inside a value that does not start with one
        Assert.Contains("stray_quote", Codes(Parse("A,B\r\n\"ab\"c,2\r\n")));                        // text after the closing quote
    }

    [Fact]
    public void A_bad_header_is_refused_with_the_column_it_names_duplicates_ignoring_capitals()
    {
        var duplicate = Parse("First,first,Last\r\nAnn,Ann,Rivera\r\n");
        Assert.Equal([("header_duplicate", "first")], duplicate.Problems.Select(p => (p.Code, p.Column ?? "")).ToArray());

        Assert.Equal(["header_empty_name"], Codes(Parse("First,,Last\r\nAnn,x,Rivera\r\n")));
        Assert.Equal(["header_name_too_long"], Codes(Parse($"{new string('n', 101)},B\r\n1,2\r\n")));
        Assert.NotNull(Parse($"{new string('n', 100)},B\r\n1,2\r\n").Table);                          // exactly 100 characters is allowed
    }

    [Fact]
    public void A_short_or_long_row_refuses_the_whole_file_rather_than_shifting_a_value_into_the_wrong_column()
    {
        var result = Parse("A,B,C\r\n1,2,3\r\n4,5\r\n6,7,8,9\r\n10,11,12\r\n");

        Assert.Equal([("row_width", 3), ("row_width", 4)], result.Problems.Select(p => (p.Code, p.Row ?? 0)).ToArray());
        Assert.Null(result.Table);
        Assert.Equal(2, result.TotalProblems);
    }

    [Fact]
    public void A_problem_never_repeats_a_value_from_the_file()
    {
        const string secret = "SECRET-PATIENT-VALUE-42";
        var results = new[]
        {
            Parse($"A,B\r\n{secret}\r\n"),                                                          // short row
            Parse($"A,B\r\n{secret}\0,2\r\n"),                                                      // control character
            Parse($"A,B\r\n{secret}\"x,2\r\n"),                                                     // stray quote
            Parse($"A,B\r\n\"{secret},2\r\n"),                                                      // unterminated
            Parse($"A,B\r\n{secret},2\r\n", new ImportOptions { MaxCellChars = 5 }),                // over-long cell
        };

        foreach (var result in results)
        {
            Assert.NotEmpty(result.Problems);
            Assert.All(result.Problems, p => Assert.DoesNotContain("SECRET", $"{p.Code} {p.Column} {p.Message}"));
        }
    }

    [Fact]
    public void A_hopeless_file_lists_at_most_the_cap_of_problems_and_counts_all_of_them()
    {
        var text = "A,B\r\n" + string.Concat(Enumerable.Repeat("only-one\r\n", 150));

        var result = Parse(text);

        Assert.Equal(ImportLimits.MaxProblemsListed, result.Problems.Count);
        Assert.Equal(150, result.TotalProblems);
        Assert.Null(result.Table);
    }

    [Fact]
    public void The_same_bytes_give_the_same_answer_every_time()
    {
        var bytes = Utf8("A,B\r\n1,2\r\n\"x\"\"y\",z\r\n");

        var first = CsvParser.Parse(bytes, Options).Table!;
        var second = CsvParser.Parse(bytes, Options).Table!;

        Assert.Equal(first.Headers, second.Headers);
        Assert.Equal(first.Rows.Select(r => string.Join("|", r)), second.Rows.Select(r => string.Join("|", r)));
    }
}
