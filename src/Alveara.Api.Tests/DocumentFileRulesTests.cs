using Alveara.Api.Architecture.Documents;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-010: the pure rules for what may be imported (docs/DOCUMENTS.md). No database, no files: every rule is exercised on its own, the happy path first, then each way it can refuse.
/// </summary>
public class DocumentFileRulesTests
{
    // ---------- the built-in blocked list ----------

    [Fact]
    public void The_built_in_blocked_list_is_exactly_the_three_named_types_and_their_twelve_close_relatives()
    {
        var expected = new[] { "exe", "html", "js", "htm", "bat", "cmd", "com", "msi", "scr", "ps1", "vbs", "dll", "jar", "hta", "lnk" };
        Assert.Equal(expected.Order(), DocumentFileRules.BuiltInBlockedExtensions.Order());
    }

    [Theory]
    [InlineData("exe")]
    [InlineData("html")]
    [InlineData("js")]
    [InlineData("htm")]
    [InlineData("bat")]
    [InlineData("cmd")]
    [InlineData("com")]
    [InlineData("msi")]
    [InlineData("scr")]
    [InlineData("ps1")]
    [InlineData("vbs")]
    [InlineData("dll")]
    [InlineData("jar")]
    [InlineData("hta")]
    [InlineData("lnk")]
    [InlineData("EXE")]
    [InlineData("Html")]
    public void Every_built_in_blocked_extension_is_refused_whatever_its_case(string extension)
    {
        Assert.Equal(DocumentErrorCodes.FileTypeBlocked, DocumentFileRules.CheckBlocked(extension, DocumentFileRules.BuiltInBlockedExtensions));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("tif")]
    [InlineData("tiff")]
    [InlineData("txt")]
    [InlineData("docx")]
    [InlineData("zip")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_types_and_a_missing_extension_are_not_blocked(string? extension)
    {
        Assert.Null(DocumentFileRules.CheckBlocked(extension, DocumentFileRules.BuiltInBlockedExtensions));
    }

    [Fact]
    public void An_extension_an_admin_added_is_blocked_too_and_the_list_it_was_added_to_is_not_the_built_in_one()
    {
        var custom = new HashSet<string>(DocumentFileRules.BuiltInBlockedExtensions) { "svg" };
        Assert.Equal(DocumentErrorCodes.FileTypeBlocked, DocumentFileRules.CheckBlocked("svg", custom));
        Assert.Equal(DocumentErrorCodes.FileTypeBlocked, DocumentFileRules.CheckBlocked("SVG", custom));
        Assert.Null(DocumentFileRules.CheckBlocked("svg", DocumentFileRules.BuiltInBlockedExtensions));
    }

    // ---------- filename normalization ----------

    [Theory]
    [InlineData("scan.pdf", "scan.pdf", "pdf")]
    [InlineData("SCAN.PDF", "SCAN.PDF", "pdf")]
    [InlineData("C:\\Users\\x\\scan.PDF", "scan.PDF", "pdf")]
    [InlineData("/tmp/a/b.png", "b.png", "png")]
    [InlineData("../../etc/passwd", "passwd", "")]
    [InlineData("..\\..\\windows\\system32\\drivers\\etc\\hosts", "hosts", "")]
    [InlineData("report.pdf.exe", "report.pdf.exe", "exe")]
    [InlineData("report.exe.pdf", "report.exe.pdf", "pdf")]
    [InlineData("photo.jpeg   ", "photo.jpeg", "jpeg")]
    [InlineData("photo.png...", "photo.png", "png")]
    [InlineData("scan.pdf:evil.exe", "scan.pdf", "pdf")]
    [InlineData("setup.exe:hidden.pdf", "setup.exe", "exe")]
    [InlineData("file.pdf::$DATA", "file.pdf", "pdf")]
    [InlineData("archive.tar.gz", "archive.tar.gz", "gz")]
    [InlineData("noextension", "noextension", "")]
    [InlineData(".exe", ".exe", "exe")]
    [InlineData("scán été.pdf", "scán été.pdf", "pdf")]
    [InlineData("Dr. Lee  X-ray 2026.PNG", "Dr. Lee  X-ray 2026.PNG", "png")]
    public void A_filename_is_reduced_to_its_last_part_without_trailing_dots_spaces_or_a_stream_suffix_and_its_extension_is_lower_case(string raw, string name, string extension)
    {
        var result = DocumentFileRules.NormalizeFileName(raw);
        Assert.True(result.IsOk, result.Error);
        Assert.Equal(name, result.Name);
        Assert.Equal(extension, result.Extension);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("folder/")]
    [InlineData("folder\\")]
    [InlineData("\0evil.pdf")]
    [InlineData("a\tb.pdf")]
    [InlineData("line\nbreak.pdf")]
    [InlineData("invoice\u202Efdp.exe")]          // a right-to-left override that makes "exe" display as "pdf"
    [InlineData("zero\u200Bwidth.pdf")]
    [InlineData("a*b.pdf")]
    [InlineData("a?b.pdf")]
    [InlineData("a<b>.pdf")]
    [InlineData("a|b.pdf")]
    [InlineData("a\"b.pdf")]
    [InlineData("CON")]
    [InlineData("con.pdf")]
    [InlineData("NUL.txt")]
    [InlineData("Aux.png")]
    [InlineData("PRN")]
    [InlineData("COM1.pdf")]
    [InlineData("lpt9")]
    public void An_unusable_filename_is_refused_with_the_name_invalid_code(string? raw)
    {
        var result = DocumentFileRules.NormalizeFileName(raw);
        Assert.False(result.IsOk);
        Assert.Equal(DocumentErrorCodes.FileNameInvalid, result.Error);
    }

    [Fact]
    public void A_name_of_exactly_255_characters_is_accepted_and_one_more_is_refused()
    {
        var ok = new string('a', 251) + ".pdf";
        Assert.Equal(255, ok.Length);
        Assert.True(DocumentFileRules.NormalizeFileName(ok).IsOk);

        var tooLong = new string('a', 252) + ".pdf";
        Assert.Equal(DocumentErrorCodes.FileNameInvalid, DocumentFileRules.NormalizeFileName(tooLong).Error);
    }

    [Fact]
    public void A_long_path_is_judged_by_the_name_alone_not_by_its_folders()
    {
        var path = string.Join("\\", Enumerable.Repeat(new string('d', 100), 10)) + "\\scan.pdf";
        Assert.True(path.Length > 255);
        Assert.Equal("scan.pdf", DocumentFileRules.NormalizeFileName(path).Name);
    }

    // ---------- content detection ----------

    private static byte[] Bytes(params int[] values) => values.Select(v => (byte)v).ToArray();

    private static byte[] WindowsProgram(int peOffset = 0x80, int length = 512)
    {
        var b = new byte[length];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        BitConverter.GetBytes(peOffset).CopyTo(b, 0x3C);
        b[peOffset] = (byte)'P'; b[peOffset + 1] = (byte)'E';
        return b;
    }

    [Fact]
    public void A_pdf_is_recognized_by_its_header_even_with_a_little_leading_junk()
    {
        Assert.Equal(DetectedContent.Pdf, DocumentFileRules.DetectContent("%PDF-1.7\n%âãÏÓ"u8));
        var withJunk = new byte[100].Concat("%PDF-1.4"u8.ToArray()).ToArray();
        Assert.Equal(DetectedContent.Pdf, DocumentFileRules.DetectContent(withJunk));
        var tooMuchJunk = new byte[2000].Concat("%PDF-1.4"u8.ToArray()).ToArray();
        Assert.Equal(DetectedContent.Unknown, DocumentFileRules.DetectContent(tooMuchJunk));
    }

    [Fact]
    public void Png_jpeg_and_tiff_are_recognized_by_their_signatures()
    {
        Assert.Equal(DetectedContent.Png, DocumentFileRules.DetectContent(Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13)));
        Assert.Equal(DetectedContent.Jpeg, DocumentFileRules.DetectContent(Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 'J', 'F', 'I', 'F')));
        Assert.Equal(DetectedContent.Jpeg, DocumentFileRules.DetectContent(Bytes(0xFF, 0xD8, 0xFF, 0xE1, 0, 8)));
        Assert.Equal(DetectedContent.Tiff, DocumentFileRules.DetectContent(Bytes('I', 'I', 0x2A, 0x00, 8, 0, 0, 0)));
        Assert.Equal(DetectedContent.Tiff, DocumentFileRules.DetectContent(Bytes('M', 'M', 0x00, 0x2A, 0, 0, 0, 8)));
        Assert.Equal(DetectedContent.Tiff, DocumentFileRules.DetectContent(Bytes('I', 'I', 0x2B, 0x00, 8, 0, 8, 0)));     // BigTIFF
    }

    [Fact]
    public void A_windows_program_is_recognized_by_its_mz_and_pe_headers()
    {
        Assert.Equal(DetectedContent.WindowsExecutable, DocumentFileRules.DetectContent(WindowsProgram()));
        Assert.Equal(DetectedContent.WindowsExecutable, DocumentFileRules.DetectContent(WindowsProgram(peOffset: 0x40)));
        Assert.Equal(DetectedContent.WindowsExecutable, DocumentFileRules.DetectContent(WindowsProgram(peOffset: 0x1F0, length: 4096)));
    }

    [Fact]
    public void Text_that_merely_starts_with_mz_is_not_a_windows_program()
    {
        Assert.Equal(DetectedContent.Unknown, DocumentFileRules.DetectContent("MZ is a patient's initials"u8));
        var noPeSignature = new byte[512];
        noPeSignature[0] = (byte)'M'; noPeSignature[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(noPeSignature, 0x3C);                    // a pointer, but nothing "PE" where it points
        Assert.Equal(DetectedContent.Unknown, DocumentFileRules.DetectContent(noPeSignature));
        var wildPointer = new byte[512];
        wildPointer[0] = (byte)'M'; wildPointer[1] = (byte)'Z';
        BitConverter.GetBytes(0x7FFFFFF0).CopyTo(wildPointer, 0x3C);                // points far outside what was read
        Assert.Equal(DetectedContent.Unknown, DocumentFileRules.DetectContent(wildPointer));
    }

    [Fact]
    public void Empty_short_and_plain_content_is_unknown()
    {
        Assert.Equal(DetectedContent.Unknown, DocumentFileRules.DetectContent(ReadOnlySpan<byte>.Empty));
        Assert.Equal(DetectedContent.Unknown, DocumentFileRules.DetectContent(Bytes(0x89)));
        Assert.Equal(DetectedContent.Unknown, DocumentFileRules.DetectContent("just some plain text"u8));
        Assert.Equal(DetectedContent.Unknown, DocumentFileRules.DetectContent("<html><body>hi</body></html>"u8));
    }

    // ---------- extension against content ----------

    [Theory]
    [InlineData("pdf", DetectedContent.Pdf)]
    [InlineData("png", DetectedContent.Png)]
    [InlineData("jpg", DetectedContent.Jpeg)]
    [InlineData("jpeg", DetectedContent.Jpeg)]
    [InlineData("tif", DetectedContent.Tiff)]
    [InlineData("tiff", DetectedContent.Tiff)]
    [InlineData("txt", DetectedContent.Unknown)]
    [InlineData("docx", DetectedContent.Unknown)]
    [InlineData("", DetectedContent.Unknown)]
    [InlineData("txt", DetectedContent.Pdf)]
    [InlineData("", DetectedContent.Png)]
    public void A_matching_claim_and_an_unverified_type_are_accepted(string extension, DetectedContent detected)
    {
        Assert.Null(DocumentFileRules.CheckContent(extension, detected));
    }

    [Theory]
    [InlineData("pdf", DetectedContent.Png)]
    [InlineData("png", DetectedContent.Pdf)]
    [InlineData("jpg", DetectedContent.Tiff)]
    [InlineData("jpeg", DetectedContent.Pdf)]
    [InlineData("tif", DetectedContent.Jpeg)]
    [InlineData("tiff", DetectedContent.Png)]
    [InlineData("pdf", DetectedContent.Unknown)]
    [InlineData("png", DetectedContent.Unknown)]
    [InlineData("JPG", DetectedContent.Unknown)]
    public void A_file_that_claims_pdf_png_jpeg_or_tiff_but_is_not_is_refused_as_a_mismatch(string extension, DetectedContent detected)
    {
        Assert.Equal(DocumentErrorCodes.FileTypeMismatch, DocumentFileRules.CheckContent(extension, detected));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("png")]
    [InlineData("txt")]
    [InlineData("")]
    [InlineData("docx")]
    public void A_windows_program_is_refused_whatever_it_is_named(string extension)
    {
        Assert.Equal(DocumentErrorCodes.ExecutableContent, DocumentFileRules.CheckContent(extension, DetectedContent.WindowsExecutable));
    }

    // ---------- what is stored and how it may be served ----------

    [Fact]
    public void The_stored_type_comes_from_the_content_and_only_pdf_png_and_jpeg_may_be_shown_inline()
    {
        Assert.Equal(new StoredTypeInfo("pdf", true, true, "application/pdf"), DocumentFileRules.Classify(DetectedContent.Pdf));
        Assert.Equal(new StoredTypeInfo("png", true, true, "image/png"), DocumentFileRules.Classify(DetectedContent.Png));
        Assert.Equal(new StoredTypeInfo("jpeg", true, true, "image/jpeg"), DocumentFileRules.Classify(DetectedContent.Jpeg));
        Assert.Equal(new StoredTypeInfo("tiff", true, false, "image/tiff"), DocumentFileRules.Classify(DetectedContent.Tiff));        // browsers do not display TIFF: download only
        Assert.Equal(new StoredTypeInfo("other", false, false, "application/octet-stream"), DocumentFileRules.Classify(DetectedContent.Unknown));
    }

    [Fact]
    public void A_windows_program_has_no_stored_type_because_it_is_never_stored()
    {
        Assert.Throws<InvalidOperationException>(() => DocumentFileRules.Classify(DetectedContent.WindowsExecutable));
    }

    // ---------- size ----------

    [Fact]
    public void An_empty_file_is_refused_and_the_limit_itself_is_allowed_but_not_one_byte_more()
    {
        const long max = 100L * 1024 * 1024;
        Assert.Equal(DocumentErrorCodes.FileEmpty, DocumentFileRules.CheckSize(0, max));
        Assert.Equal(DocumentErrorCodes.FileEmpty, DocumentFileRules.CheckSize(-1, max));
        Assert.Null(DocumentFileRules.CheckSize(1, max));
        Assert.Null(DocumentFileRules.CheckSize(max, max));
        Assert.Equal(DocumentErrorCodes.FileTooLarge, DocumentFileRules.CheckSize(max + 1, max));
    }

    [Fact]
    public void The_default_maximum_is_100_megabytes_and_inside_the_allowed_range()
    {
        Assert.Equal(100L * 1024 * 1024, DocumentFileRules.DefaultMaxUploadBytes);
        Assert.Null(DocumentFileRules.ValidateMaxUploadBytes(DocumentFileRules.DefaultMaxUploadBytes));
    }

    [Theory]
    [InlineData(1_048_576L, true)]
    [InlineData(104_857_600L, true)]
    [InlineData(1_073_741_824L, true)]
    [InlineData(1_048_575L, false)]
    [InlineData(1_073_741_825L, false)]
    [InlineData(0L, false)]
    [InlineData(-5L, false)]
    [InlineData(long.MaxValue, false)]
    public void A_configured_maximum_must_be_between_1_megabyte_and_1_gigabyte(long maxBytes, bool valid)
    {
        var error = DocumentFileRules.ValidateMaxUploadBytes(maxBytes);
        if (valid) Assert.Null(error);
        else Assert.Equal(DocumentErrorCodes.MetadataInvalid, error);
    }

    // ---------- an admin adding a blocked extension ----------

    [Theory]
    [InlineData("svg", "svg")]
    [InlineData("SVG", "svg")]
    [InlineData(".Svg", "svg")]
    [InlineData("  php ", "php")]
    [InlineData("7z", "7z")]
    [InlineData("a", "a")]
    [InlineData("abcdefghijklmnop", "abcdefghijklmnop")]
    public void An_extension_to_block_is_normalized_to_lower_case_without_a_dot(string raw, string expected)
    {
        var result = DocumentFileRules.NormalizeBlockedExtensionInput(raw);
        Assert.True(result.IsOk, result.Error);
        Assert.Equal(expected, result.Extension);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("tar.gz")]
    [InlineData("a b")]
    [InlineData("exe/")]
    [InlineData("é")]
    [InlineData("abcdefghijklmnopq")]
    [InlineData("<script>")]
    public void An_extension_that_is_not_plain_letters_and_digits_is_refused(string? raw)
    {
        var result = DocumentFileRules.NormalizeBlockedExtensionInput(raw);
        Assert.False(result.IsOk);
        Assert.Equal(DocumentErrorCodes.MetadataInvalid, result.Error);
    }
}
