using System.Text;
using Microsoft.Extensions.Configuration;
using SkiaSharp;
using Alveara.Api.Architecture.LoginPresentation;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N020-C01 (review corrections 1 and 2): the upload pipeline on its own. Only PNG/JPEG by content (extension must agree); SVG, GIF, WebP, BMP and text are
/// refused before any decoder runs; size, per-side, total-pixel and animation limits; damaged files refused; the stored derivative is a fresh encode with none of
/// the input's metadata (EXIF, XMP, IPTC, PNG text/eXIf chunks), scaled down above 2,560 px.
/// </summary>
public class LoginBackgroundPipelineTests
{
    private static string Code(Action act) => Assert.Throws<LoginBackgroundRefusedException>(act).Code;

    [Fact]
    public void A_PNG_becomes_a_PNG_derivative_with_its_size_and_a_lower_case_hash()
    {
        var d = LoginBackgroundPipeline.Process(LoginTestImages.Png(600, 400), "teal.png");
        Assert.Equal(("image/png", "png", 600, 400), (d.ContentType, d.Extension, d.Width, d.Height));
        Assert.Matches("^[0-9a-f]{64}$", d.Sha256);
        Assert.Equal(SKEncodedImageFormat.Png, SKCodec.Create(SKData.CreateCopy(d.Bytes))!.EncodedFormat);
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    public void A_JPEG_becomes_a_JPEG_derivative(string name)
    {
        var d = LoginBackgroundPipeline.Process(LoginTestImages.Jpeg(800, 600), name);
        Assert.Equal(("image/jpeg", "jpg", 800, 600), (d.ContentType, d.Extension, d.Width, d.Height));
    }

    public static TheoryData<string, byte[], string> RefusedTypes => new()
    {
        { "SVG", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"400\" height=\"400\"><script>alert(1)</script></svg>"), "bg.svg" },
        { "SVG renamed .png", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), "bg.png" },
        { "GIF", [.. "GIF89a"u8, 1, 1, 1, 1, 0, 0, 0, .. new byte[40]], "bg.gif" },
        { "WebP", [.. "RIFF"u8, 40, 0, 0, 0, .. "WEBPVP8 "u8, .. new byte[40]], "bg.webp" },
        { "BMP", [.. "BM"u8, .. new byte[60]], "bg.bmp" },
        { "text renamed .png", Encoding.UTF8.GetBytes("just some text, not an image at all"), "bg.png" },
    };

    [Theory]
    [MemberData(nameof(RefusedTypes))]
    public void Anything_but_PNG_or_JPEG_content_is_refused_before_decoding(string _, byte[] bytes, string name) =>
        Assert.Equal("background_type_refused", Code(() => LoginBackgroundPipeline.Process(bytes, name)));

    [Theory]
    [InlineData("bg.jpg")]
    [InlineData("bg.gif")]
    [InlineData("bg")]
    public void A_PNG_whose_name_claims_another_type_is_refused(string name) =>
        Assert.Equal("background_type_mismatch", Code(() => LoginBackgroundPipeline.Process(LoginTestImages.Png(300, 300), name)));

    [Fact]
    public void Size_limits_5_MB_and_empty()
    {
        Assert.Equal("background_empty", Code(() => LoginBackgroundPipeline.Process([], "a.png")));
        var tooBig = new byte[LoginBackgroundPipeline.MaxInputBytes + 1];
        LoginTestImages.Png(300, 300).CopyTo(tooBig, 0);
        Assert.Equal("background_too_large", Code(() => LoginBackgroundPipeline.Process(tooBig, "a.png")));
    }

    [Theory]
    [InlineData(255, 300, "background_dimensions")]
    [InlineData(300, 255, "background_dimensions")]
    [InlineData(4097, 300, "background_dimensions")]
    public void Each_side_must_be_256_to_4096_pixels(int w, int h, string code) =>
        Assert.Equal(code, Code(() => LoginBackgroundPipeline.Process(LoginTestImages.Png(w, h), "a.png")));

    [Fact]
    public void Boundary_sides_256_and_4096_are_accepted_and_a_large_image_is_scaled_to_2560()
    {
        Assert.Equal(256, LoginBackgroundPipeline.Process(LoginTestImages.Png(256, 256), "a.png").Width);
        var big = LoginBackgroundPipeline.Process(LoginTestImages.Jpeg(4096, 2048), "a.jpg");
        Assert.Equal((2560, 1280), (big.Width, big.Height));
    }

    [Fact]
    public void An_animated_PNG_is_refused_not_flattened() =>
        Assert.Equal("background_animated_refused", Code(() => LoginBackgroundPipeline.Process(LoginTestImages.AnimatedPng(300, 300), "a.png")));

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    public void A_truncated_image_is_refused_as_unreadable(string kind)
    {
        var bytes = kind == "png" ? LoginTestImages.Png(400, 400) : LoginTestImages.Jpeg(400, 400);
        Assert.Equal("background_unreadable", Code(() => LoginBackgroundPipeline.Process(LoginTestImages.Truncated(bytes), $"a.{kind}")));
    }

    [Fact]
    public void JPEG_metadata_EXIF_XMP_and_IPTC_is_not_in_the_derivative()
    {
        var input = LoginTestImages.JpegWithExifXmpIptc(500, 400);
        Assert.Contains((byte)0xE1, LoginTestImages.JpegHeaderMarkers(input));                              // the input really carries it
        var d = LoginBackgroundPipeline.Process(input, "camera.jpg");

        var markers = LoginTestImages.JpegHeaderMarkers(d.Bytes);
        Assert.DoesNotContain((byte)0xE1, markers);                                                         // APP1: EXIF and XMP
        Assert.DoesNotContain((byte)0xED, markers);                                                         // APP13: IPTC
        var text = Encoding.ASCII.GetString(d.Bytes);
        foreach (var leaked in new[] { "Exif", "GPSLatitude", "xmpmeta", "secret-camera-owner", "Photoshop", "caption-patient-name" })
            Assert.DoesNotContain(leaked, text);
    }

    [Fact]
    public void PNG_text_and_eXIf_chunks_are_not_in_the_derivative()
    {
        var input = LoginTestImages.PngWithTextChunks(400, 300);
        Assert.Contains("tEXt", LoginTestImages.PngChunkTypes(input));
        var d = LoginBackgroundPipeline.Process(input, "scan.png");

        var chunks = LoginTestImages.PngChunkTypes(d.Bytes);
        foreach (var forbidden in new[] { "tEXt", "iTXt", "zTXt", "eXIf", "tIME", "acTL" })
            Assert.DoesNotContain(forbidden, chunks);
        var text = Encoding.ASCII.GetString(d.Bytes);
        Assert.DoesNotContain("patient-name-in-text", text);
        Assert.DoesNotContain("GPS-in-png", text);
    }

    [Fact]
    public void The_same_input_always_gives_the_same_derivative_hash()
    {
        var input = LoginTestImages.Png(320, 320);
        Assert.Equal(LoginBackgroundPipeline.Process(input, "a.png").Sha256, LoginBackgroundPipeline.Process(input, "b.png").Sha256);
    }
}

/// <summary>ALV-N020-C01: the plain-text footer notice rules.</summary>
public class LoginNoticeRulesTests
{
    [Fact]
    public void Text_is_trimmed_and_line_endings_normalized_and_empty_means_no_custom_text()
    {
        Assert.Equal("Line one\nLine two", LoginNoticeRules.Check("  Line one\r\nLine two  ").Text);
        var empty = LoginNoticeRules.Check("   \r\n ");
        Assert.True(empty.Ok);
        Assert.Null(empty.Text);
    }

    [Fact]
    public void Length_1000_is_accepted_and_1001_refused()
    {
        Assert.True(LoginNoticeRules.Check(new string('a', 1000)).Ok);
        Assert.Equal("notice_too_long", LoginNoticeRules.Check(new string('a', 1001)).ErrorCode);
    }

    [Fact]
    public void Ten_line_breaks_are_accepted_and_eleven_refused()
    {
        Assert.True(LoginNoticeRules.Check(string.Join("\n", Enumerable.Repeat("x", 11))).Ok);
        Assert.Equal("notice_too_many_lines", LoginNoticeRules.Check(string.Join("\n", Enumerable.Repeat("x", 12))).ErrorCode);
    }

    [Theory]
    [InlineData("tab\there", "notice_control_character")]
    [InlineData("bell\u0007", "notice_control_character")]
    [InlineData("<script>alert(1)</script>", "notice_markup")]
    [InlineData("a > b", "notice_markup")]
    [InlineData("see https://evil.example", "notice_link")]
    [InlineData("visit www.example.com", "notice_link")]
    [InlineData("mailto:someone@example.com", "notice_link")]
    public void Markup_links_and_control_characters_are_refused(string text, string code) =>
        Assert.Equal(code, LoginNoticeRules.Check(text).ErrorCode);

    [Fact]
    public void The_shipped_USA_default_passes_the_rules()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false).Build();
        var check = LoginNoticeRules.Check(config["LoginPresentation:DefaultFooterNotice"]);
        Assert.True(check.Ok, check.ErrorMessage);
        Assert.StartsWith("Please ensure you have taken appropriate precautions", check.Text);
        Assert.Contains("\n\nAll access to this system will be tracked", check.Text);
    }
}
