using System.Security.Cryptography;
using SkiaSharp;
using Alveara.Api.Architecture.Documents;

namespace Alveara.Api.Architecture.LoginPresentation;

/// <summary>A refused upload: a stable code for the API and a plain message for the administrator.</summary>
public sealed class LoginBackgroundRefusedException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>The server-generated derivative: only these bytes are ever stored or served.</summary>
public sealed record LoginBackgroundDerivative(byte[] Bytes, string ContentType, string Extension, int Width, int Height, string Sha256);

/// <summary>
/// ALV-N020-C01 (review corrections 1 and 2): turns an administrator's upload into a clean derivative, or refuses it. Order: size cap; content-detected type
/// (PNG or JPEG only, extension must agree; SVG, GIF, WebP and everything else refused before any decoder runs); header-only dimension, total-pixel and frame
/// checks with SkiaSharp's codec (nothing decoded yet); a bounded decode of exactly those pixels into sRGB; a fresh encode of the pixels only (PNG stays PNG,
/// JPEG becomes JPEG quality 90), scaled down when the long side exceeds 2,560 px. No metadata is copied from the input; the original bytes are never kept.
/// Decoding is synchronous and bounded by the pixel limit (at most 4,096 x 4,096 RGBA = 64 MiB), so it cannot run unbounded.
/// </summary>
public static class LoginBackgroundPipeline
{
    public const int MaxInputBytes = 5 * 1024 * 1024;
    public const int MinSide = 256;
    public const int MaxSide = 4096;
    public const long MaxTotalPixels = 4096L * 4096L;
    public const int MaxDerivativeLongSide = 2560;
    public const int JpegQuality = 90;

    /// <summary>
    /// An APNG carries an "acTL" chunk before its first image data. SkiaSharp's PNG decoder would quietly read only the first frame, so the chunk list is walked
    /// here (bounded: chunk lengths are checked against the buffer) and an animation is refused rather than flattened. A broken chunk structure is unreadable.
    /// </summary>
    public static bool IsAnimatedPng(ReadOnlySpan<byte> png)
    {
        var at = 8;                                                                                         // after the signature
        while (at + 8 <= png.Length)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.Slice(at, 4));
            var type = png.Slice(at + 4, 4);
            if (type.SequenceEqual("acTL"u8)) return true;
            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8)) return false;                  // acTL must come before the image data
            if (length > (uint)(png.Length - at - 12))
                throw new LoginBackgroundRefusedException("background_unreadable", "The image could not be read. It may be damaged or incomplete.");
            at += 12 + (int)length;                                                                         // length + type + data + CRC
        }
        return false;
    }

    public static LoginBackgroundDerivative Process(byte[] input, string? fileName)
    {
        if (input.Length == 0) throw new LoginBackgroundRefusedException("background_empty", "The file is empty.");
        if (input.Length > MaxInputBytes) throw new LoginBackgroundRefusedException("background_too_large", "The image can be at most 5 MB.", 413);

        var detected = DocumentFileRules.DetectContent(input.AsSpan(0, Math.Min(input.Length, 1024)));
        if (detected is not (DetectedContent.Png or DetectedContent.Jpeg))
            throw new LoginBackgroundRefusedException("background_type_refused", "Only PNG and JPEG images can be added (not SVG, GIF, WebP or other files).", 415);
        var claimed = Path.GetExtension(fileName ?? string.Empty).TrimStart('.').ToLowerInvariant() switch
        {
            "png" => DetectedContent.Png,
            "jpg" or "jpeg" => DetectedContent.Jpeg,
            _ => DetectedContent.Unknown,
        };
        if (claimed != detected)
            throw new LoginBackgroundRefusedException("background_type_mismatch", "The file name must end in .png, .jpg or .jpeg and match the image's real type.", 415);

        if (detected == DetectedContent.Png && IsAnimatedPng(input))
            throw new LoginBackgroundRefusedException("background_animated_refused", "Animated images cannot be used as a background.");

        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data)
            ?? throw new LoginBackgroundRefusedException("background_unreadable", "The image could not be read. It may be damaged or incomplete.");
        var info = codec.Info;
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg))
            throw new LoginBackgroundRefusedException("background_type_refused", "Only PNG and JPEG images can be added (not SVG, GIF, WebP or other files).", 415);
        if (info.Width < MinSide || info.Height < MinSide || info.Width > MaxSide || info.Height > MaxSide)
            throw new LoginBackgroundRefusedException("background_dimensions", $"Each side must be between {MinSide} and {MaxSide:N0} pixels.");
        if ((long)info.Width * info.Height > MaxTotalPixels)
            throw new LoginBackgroundRefusedException("background_too_many_pixels", "The image has too many pixels.");
        if (codec.FrameCount > 1)
            throw new LoginBackgroundRefusedException("background_animated_refused", "Animated images cannot be used as a background.");

        var opaque = detected == DetectedContent.Jpeg;
        var decodeInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        using var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (result != SKCodecResult.Success)
            throw new LoginBackgroundRefusedException("background_unreadable", "The image could not be read. It may be damaged or incomplete.");

        var bitmap = decoded;
        SKBitmap? scaled = null;
        try
        {
            var longSide = Math.Max(info.Width, info.Height);
            if (longSide > MaxDerivativeLongSide)
            {
                var factor = (double)MaxDerivativeLongSide / longSide;
                var target = decodeInfo.WithSize(Math.Max(1, (int)Math.Round(info.Width * factor)), Math.Max(1, (int)Math.Round(info.Height * factor)));
                scaled = decoded.Resize(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                    ?? throw new LoginBackgroundRefusedException("background_unreadable", "The image could not be processed.");
                bitmap = scaled;
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = opaque ? image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality) : image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded is null) throw new LoginBackgroundRefusedException("background_unreadable", "The image could not be processed.");
            var bytes = encoded.ToArray();
            return new LoginBackgroundDerivative(bytes, opaque ? "image/jpeg" : "image/png", opaque ? "jpg" : "png", bitmap.Width, bitmap.Height,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        finally
        {
            scaled?.Dispose();
        }
    }
}
