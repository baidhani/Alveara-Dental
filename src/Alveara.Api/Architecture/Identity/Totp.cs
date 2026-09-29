using System.Runtime.CompilerServices;
using OtpNet;

[assembly: InternalsVisibleTo("Alveara.Api.Tests")]

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-001-C01: RFC 6238 TOTP (HMAC-SHA1, 30-second step, 6 digits) — the "at least one MFA
/// factor that works with no public internet" requirement. TOTP is entirely local: no request
/// leaves the machine to enroll, challenge, or verify a code, unlike email/SMS OTP.
///
/// R01 review finding ALV-001-C01-R01-03: the original implementation hand-rolled Base32
/// encoding, RFC 4226 dynamic truncation, and TOTP windowing directly against the .NET BCL,
/// which the project's engineering reference explicitly prohibits ("no custom crypto — use a
/// maintained, well-reviewed platform/library facility"). This is now a thin wrapper over
/// Otp.NET (github.com/kspearrin/Otp.NET), a widely used, actively maintained TOTP/HOTP library
/// — this file contains no cryptographic algorithm of its own.
/// </summary>
public static class Totp
{
    private const int StepSeconds = 30;
    private const int Digits = 6;

    /// <summary>A fresh random 160-bit secret, the size RFC 4226 recommends for HMAC-SHA1.</summary>
    public static byte[] GenerateSecret() => KeyGeneration.GenerateRandomKey(20);

    public static string ToBase32(byte[] secret) => Base32Encoding.ToString(secret);

    /// <summary>Test-only: computes the code a real authenticator app would show right now for this
    /// secret, so tests can drive enrollment/challenge without a real device.</summary>
    internal static string GenerateCodeForTests(byte[] secret, DateTimeOffset? atUtc = null) =>
        new OtpNet.Totp(secret, step: StepSeconds, totpSize: Digits).ComputeTotp((atUtc ?? DateTimeOffset.UtcNow).UtcDateTime);

    /// <summary>Test-only: decodes the Base32 secret <see cref="ToBase32"/> hands to an
    /// authenticator app, so tests can recover the raw secret bytes to compute codes with.</summary>
    internal static byte[] FromBase32(string base32) => Base32Encoding.ToBytes(base32);

    /// <summary>Verifies a submitted code against the secret, tolerating +/- one 30s step of clock skew.</summary>
    public static bool Verify(byte[] secret, string submittedCode, DateTimeOffset? atUtc = null)
    {
        if (string.IsNullOrWhiteSpace(submittedCode) || submittedCode.Length != Digits || !submittedCode.All(char.IsDigit))
        {
            return false;
        }

        var totp = new OtpNet.Totp(secret, step: StepSeconds, totpSize: Digits);
        var window = new VerificationWindow(previous: 1, future: 1);
        return totp.VerifyTotp(atUtc?.UtcDateTime ?? DateTime.UtcNow, submittedCode, out _, window);
    }
}
