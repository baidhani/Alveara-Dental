using System.Security.Cryptography;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// STORY-001/REQ-001's "secure password storage": PBKDF2-HMAC-SHA256 with a random salt per
/// password and a work factor high enough to resist offline brute-forcing, using only the
/// .NET BCL (no external dependency). The encoded hash is self-describing (iteration count and
/// salt travel with it), so the work factor can be raised in the future without invalidating
/// hashes stored under a lower one.
/// </summary>
public static class Pbkdf2PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32;
    private const int Iterations = 210_000; // OWASP-recommended floor for PBKDF2-HMAC-SHA256 as of 2023.

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string encodedHash)
    {
        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256")
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedKey;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedKey = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedKey.Length);
        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}
