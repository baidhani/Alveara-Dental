using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Documents;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01 against a real on-disk store: what a user-supplied file name and a file's content can and cannot do. A name that tries to reach another folder, name a device, hide a type, carry control or invisible
/// characters or run too long is normalized or refused (the table below states which and why); however it is written, the stored file is named by its generated id inside the storage folder and nothing is created
/// anywhere else; and the type of a file comes from its content, so a program, a web page or a picture of another type cannot pass under a harmless name.
/// </summary>
public class DocumentPathSafetyTests : DocumentTestBase
{
    private static readonly string[] TraversalTargets = ["x.png", "passwd.png", "win.png", "evil.png", "evil.txt"];

    public static TheoryData<string, string> Names => new()
    {
        // normalized: the folder part is dropped and only the last part is kept as the name
        { "../../x.png", "ok:x.png" }, { @"..\..\x.png", "ok:x.png" }, { "/etc/passwd.png", "ok:passwd.png" }, { @"C:\Windows\win.png", "ok:win.png" }, { "a/b/c.png", "ok:c.png" }, { @"a\b\c.png", "ok:c.png" },
        { "x.png ", "ok:x.png" }, { "x.png.", "ok:x.png" }, { "name:stream.png", "ok:name" }, { "file.PNG", "ok:file.PNG" }, { "résumé.png", "ok:résumé.png" }, { "emoji😀.png", "ok:emoji😀.png" },
        { ".htaccess", "ok:.htaccess" }, { ".png", "ok:.png" }, { "%2e%2e%2fevil.png", "ok:%2e%2e%2fevil.png" },
        // refused as a name
        { "..", "err:file_name_invalid" }, { ".", "err:file_name_invalid" }, { "...", "err:file_name_invalid" }, { "   ", "err:file_name_invalid" }, { "con.png", "err:file_name_invalid" }, { "AUX", "err:file_name_invalid" },
        { "nul.png", "err:file_name_invalid" }, { "COM1.png", "err:file_name_invalid" }, { "x.png\0.exe", "err:file_name_invalid" }, { "a\tb.png", "err:file_name_invalid" }, { "a\nb.png", "err:file_name_invalid" },
        { "\u202Egpj.exe", "err:file_name_invalid" }, { "zero\u200Bwidth.png", "err:file_name_invalid" }, { "<script>.png", "err:file_name_invalid" }, { "a|b.png", "err:file_name_invalid" }, { "a?b.png", "err:file_name_invalid" },
        { "a*b.png", "err:file_name_invalid" }, { "a\"b.png", "err:file_name_invalid" },
        // refused as a type, however it is dressed
        { "payload.exe", "err:file_type_blocked" }, { "payload.png.exe", "err:file_type_blocked" }, { "payload.exe ", "err:file_type_blocked" }, { "payload.EXE", "err:file_type_blocked" }, { "payload.exe.", "err:file_type_blocked" },
        { "../../evil.exe", "err:file_type_blocked" }, { @"..\evil.html", "err:file_type_blocked" }, { "page.HTM", "err:file_type_blocked" }, { "run.js", "err:file_type_blocked" }, { "setup.msi", "err:file_type_blocked" },
        { "x.exe::$DATA", "err:file_type_blocked" },
    };

    [Theory]
    [MemberData(nameof(Names))]
    public async Task A_file_name_is_normalized_or_refused_and_never_decides_where_the_file_goes(string name, string expected)
    {
        var parentBefore = Directory.GetFiles(Path.GetDirectoryName(_storageRoot)!).Length;
        var request = Request(Png(150), name);
        if (expected.StartsWith("err:"))
        {
            var e = await Assert.ThrowsAsync<DocumentException>(() => ImportAsync(request));
            Assert.Equal(expected[4..], e.Code);
            await AssertNothingRecordedAsync();
        }
        else
        {
            var doc = (await ImportAsync(request)).Document;
            Assert.Equal(expected[3..], doc.OriginalFileName);
            Assert.DoesNotContain(doc.OriginalFileName, new[] { "/", "\\" }.Where(s => doc.OriginalFileName.Contains(s)));       // no path separator survives in the stored name
        }

        // wherever the name pointed, the only thing on disk is a file named by a generated id inside the storage folder
        Assert.All(Directory.GetFiles(_storageRoot), f => Assert.True(Guid.TryParseExact(Path.GetFileName(f), "N", out _), $"unexpected file {f}"));
        Assert.Empty(Directory.GetDirectories(_storageRoot));
        var parent = Path.GetDirectoryName(_storageRoot)!;
        Assert.Equal(parentBefore, Directory.GetFiles(parent).Length);
        foreach (var target in TraversalTargets) Assert.False(File.Exists(Path.Combine(parent, target)) || File.Exists(Path.Combine(parent, "..", target)), $"{target} was created outside the storage folder");
    }

    [Fact]
    public async Task A_name_of_exactly_two_hundred_and_fifty_five_characters_is_accepted_and_one_more_is_refused()
    {
        var ok = new string('n', 251) + ".png";
        Assert.Equal(255, ok.Length);
        Assert.Equal(ok, (await ImportAsync(Request(Png(151), ok))).Document.OriginalFileName);
        var e = await Assert.ThrowsAsync<DocumentException>(() => ImportAsync(Request(Png(152), "n" + ok)));
        Assert.Equal("file_name_invalid", e.Code);
    }

    [Fact]
    public async Task The_stored_file_is_named_by_its_generated_id_not_by_anything_the_user_typed()
    {
        var doc = (await ImportAsync(Request(Png(153), "ann-okafor-bitewing.png"))).Document;
        var blob = (await WithDb(async db => await db.Documents.AsNoTracking().SingleAsync(d => d.Id == doc.Id))).BlobId;
        Assert.Equal([blob.ToString("N")], Directory.GetFiles(_storageRoot).Select(Path.GetFileName));
    }

    // ---------- the type comes from the content ----------

    public static TheoryData<string, string, string> Contents => new()
    {
        { "html-as-png", "page.png", "err:file_type_mismatch" }, { "program-as-png", "photo.png", "err:executable_content" }, { "program-as-pdf", "letter.pdf", "err:executable_content" },
        { "program-as-text", "notes.txt", "err:executable_content" }, { "program-as-nothing", "noextension", "err:executable_content" }, { "pdf-as-png", "scan.png", "err:file_type_mismatch" },
        { "jpeg-as-png", "scan.png", "err:file_type_mismatch" }, { "png-as-pdf", "scan.pdf", "err:file_type_mismatch" }, { "png-as-jpg", "scan.jpg", "err:file_type_mismatch" },
        { "empty-png", "scan.png", "err:file_empty" }, { "html-as-text", "notes.txt", "ok:other" }, { "script-as-text", "notes.txt", "ok:other" }, { "png-as-text", "notes.txt", "ok:png" },
        { "png-no-extension", "noextension", "ok:png" }, { "pdf-with-junk-before", "letter.pdf", "ok:pdf" },
    };

    private static byte[] ContentOf(string kind)
    {
        byte[] program = new byte[0x100]; program[0] = (byte)'M'; program[1] = (byte)'Z'; program[0x3C] = 0x80; program[0x80] = (byte)'P'; program[0x81] = (byte)'E';
        return kind switch
        {
            "html-as-png" or "html-as-text" => "<html><script>alert(1)</script></html>"u8.ToArray(),
            "script-as-text" => "<script>alert(1)</script>"u8.ToArray(),
            "program-as-png" or "program-as-pdf" or "program-as-text" or "program-as-nothing" => program,
            "pdf-as-png" or "pdf-with-junk-before" => [.. (kind == "pdf-with-junk-before" ? "JUNK\n"u8.ToArray() : []), .. "%PDF-1.4\nbody"u8.ToArray()],
            "jpeg-as-png" => [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4],
            "empty-png" => [],
            _ => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4],
        };
    }

    [Theory]
    [MemberData(nameof(Contents))]
    public async Task The_type_of_a_file_comes_from_its_content_so_a_program_a_page_or_a_picture_of_another_type_cannot_pass_under_a_harmless_name(string kind, string name, string expected)
    {
        var request = Request(ContentOf(kind), name);
        if (expected.StartsWith("err:"))
        {
            var e = await Assert.ThrowsAsync<DocumentException>(() => ImportAsync(request));
            Assert.Equal(expected[4..], e.Code);
            await AssertNothingRecordedAsync();
        }
        else
        {
            var doc = (await ImportAsync(request)).Document;
            Assert.Equal(expected[3..], doc.DetectedType);
            if (kind is "html-as-text" or "script-as-text") Assert.False(doc.TypeVerified);                                  // stored, but labelled "type not verified" and served only as a download
        }
    }
}
