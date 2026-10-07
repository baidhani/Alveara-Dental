using System.Security.Cryptography;
using System.Threading;

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

    // ALV-001-C01: a fixed, valid-shaped hash Verify runs against for a login whose username
    // doesn't exist, so that branch pays the same PBKDF2 work a real (wrong-password) verification
    // would — the specific fix for the timing-based username-enumeration side channel. Computed
    // once, from a value that is not a real password and is never used for anything else.
    public static readonly string DummyHashForUnknownUser = Hash("dummy-hash-for-unknown-user-timing-parity");

    // Test-visible instrumentation only: proves structurally (call count) that the unknown-user
    // and wrong-password login paths perform the same number of PBKDF2 verifications, rather than
    // relying solely on a flaky wall-clock timing assertion. A process-wide counter only gives a
    // reliable answer if nothing else in the process calls Verify concurrently while it's being
    // measured - AccountServiceLoginTests is in the "serial-server" xUnit collection (DisableParallelization, see
    // ParallelismCollections.cs in Alveara.Api.Tests), so it never overlaps another test class.
    private static long _verifyCallCount;
    public static long VerifyCallCount => Interlocked.Read(ref _verifyCallCount);
    public static void ResetVerifyCallCountForTests() => Interlocked.Exchange(ref _verifyCallCount, 0);

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string encodedHash)
    {
        Interlocked.Increment(ref _verifyCallCount);

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
