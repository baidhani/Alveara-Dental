using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Alveara.Api.Tests;

/// <summary>ALV-N020-C01: test images built in code (no binary fixtures): real PNG/JPEG encodings plus deliberately injected metadata, animation and damage.</summary>
internal static class LoginTestImages
{
    public static byte[] Png(int width, int height, SKColor? color = null) => Encode(width, height, SKEncodedImageFormat.Png, color ?? new SKColor(10, 80, 90));

    public static byte[] Jpeg(int width, int height, SKColor? color = null) => Encode(width, height, SKEncodedImageFormat.Jpeg, color ?? new SKColor(200, 110, 90));

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format, SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 4f, paint);              // some real content, so encodings differ by size
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 92);
        return data.ToArray();
    }

    /// <summary>Inserts a PNG chunk right after IHDR (signature 8 + IHDR 25 bytes), with a correct CRC.</summary>
    public static byte[] WithPngChunk(byte[] png, string type, byte[] payload)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)payload.Length);
        typeBytes.CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + payload.Length), Crc32(chunk.AsSpan(4, 4 + payload.Length)));
        return [.. png[..33], .. chunk, .. png[33..]];
    }

    /// <summary>Inserts a JPEG marker segment right after SOI (FF D8).</summary>
    public static byte[] WithJpegSegment(byte[] jpeg, byte marker, byte[] payload)
    {
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);
        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }

    public static byte[] JpegWithExifXmpIptc(int width, int height)
    {
        var jpeg = Jpeg(width, height);
        var exif = Encoding.ASCII.GetBytes("Exif\0\0MM\0*\0\0\0\bGPSLatitude=38.8977;GPSLongitude=-77.0365");
        var xmp = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta xmlns:x='adobe:ns:meta/'>secret-camera-owner</x:xmpmeta>");
        var iptc = Encoding.ASCII.GetBytes("Photoshop 3.0\08BIM\u0004\u0004\0\0\0\0\0\u0010caption-patient-name");
        return WithJpegSegment(WithJpegSegment(WithJpegSegment(jpeg, 0xED, iptc), 0xE1, xmp), 0xE1, exif);
    }

    public static byte[] PngWithTextChunks(int width, int height)
    {
        var png = Png(width, height);
        png = WithPngChunk(png, "tEXt", Encoding.ASCII.GetBytes("Comment\0patient-name-in-text"));
        png = WithPngChunk(png, "iTXt", Encoding.ASCII.GetBytes("XML:com.adobe.xmp\0\0\0\0\0<x:xmpmeta>owner</x:xmpmeta>"));
        return WithPngChunk(png, "eXIf", Encoding.ASCII.GetBytes("MM\0*\0\0\0\bGPS-in-png"));
    }

    /// <summary>A PNG that announces animation (an acTL chunk: 2 frames, loop forever).</summary>
    public static byte[] AnimatedPng(int width, int height)
    {
        var actl = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl, 2);
        return WithPngChunk(Png(width, height), "acTL", actl);
    }

    public static byte[] Truncated(byte[] image) => image[..(image.Length / 2)];

    /// <summary>The chunk types of a PNG, in order.</summary>
    public static List<string> PngChunkTypes(byte[] png)
    {
        var types = new List<string>();
        var at = 8;
        while (at + 8 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at, 4));
            types.Add(Encoding.ASCII.GetString(png, at + 4, 4));
            at += 12 + length;
        }
        return types;
    }

    /// <summary>The APPn/COM marker bytes of a JPEG's header segments (up to start of scan).</summary>
    public static List<byte> JpegHeaderMarkers(byte[] jpeg)
    {
        var markers = new List<byte>();
        var at = 2;
        while (at + 4 <= jpeg.Length && jpeg[at] == 0xFF)
        {
            var marker = jpeg[at + 1];
            if (marker == 0xDA) break;                                                                      // start of scan: image data follows
            markers.Add(marker);
            at += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 2, 2));
        }
        return markers;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
