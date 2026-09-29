using System.Security.Cryptography;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-001-C01: RFC 6238 TOTP (HMAC-SHA1, 30-second step, 6 digits) — the "at least one MFA
/// factor that works with no public internet" requirement. TOTP is entirely local: no request
/// leaves the machine to enroll, challenge, or verify a code, unlike email/SMS OTP. Implemented
/// directly against the .NET BCL (no external package) so the dependency surface stays small.
/// </summary>
public static class Totp
{
    private const int StepSeconds = 30;
    private const int Digits = 6;
    private const int ToleranceSteps = 1; // accepts the previous/current/next 30s window, for clock skew

    /// <summary>A fresh random 160-bit secret, the size RFC 4226 recommends for HMAC-SHA1.</summary>
    public static byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(20);

    public static string ToBase32(byte[] secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new System.Text.StringBuilder();
        int bits = 0, value = 0;
        foreach (var b in secret)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(alphabet[(value >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }
        if (bits > 0)
        {
            result.Append(alphabet[(value << (5 - bits)) & 0x1F]);
        }
        return result.ToString();
    }

    private static string ComputeCode(byte[] secret, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);
        var code = binaryCode % (int)Math.Pow(10, Digits);
        return code.ToString().PadLeft(Digits, '0');
    }

    /// <summary>Verifies a submitted code against the secret, tolerating +/- one 30s step of clock skew.</summary>
    public static bool Verify(byte[] secret, string submittedCode, DateTimeOffset? atUtc = null)
    {
        if (string.IsNullOrWhiteSpace(submittedCode) || submittedCode.Length != Digits || !submittedCode.All(char.IsDigit))
        {
            return false;
        }

        var now = atUtc ?? DateTimeOffset.UtcNow;
        var currentCounter = now.ToUnixTimeSeconds() / StepSeconds;

        for (var offset = -ToleranceSteps; offset <= ToleranceSteps; offset++)
        {
            var candidate = ComputeCode(secret, currentCounter + offset);
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(candidate),
                    System.Text.Encoding.ASCII.GetBytes(submittedCode)))
            {
                return true;
            }
        }

        return false;
    }
}
