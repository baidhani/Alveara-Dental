using System.Security.Cryptography;
using System.Text;
using Alveara.Api.Architecture.Backup;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N004 encryption/authentication/recovery-material tests. These exercise the framing around
/// the platform's AES-GCM/RSA-OAEP, and prove the properties the story requires: confidentiality,
/// authentication (tamper/truncate/extend/reorder all fail), wrong-key detection, and that the server
/// holds only the public half of the recovery key.
/// </summary>
public class BackupCryptoTests
{
    private const string Passphrase = "correct horse battery staple 42";

    // Generating RSA-3072 keys is slow-ish, so share one pair across the tests in this class.
    private static readonly Lazy<RecoveryKeyMaterial> SharedKey = new(() => BackupCrypto.GenerateRecoveryKey(Passphrase));
    private static RecoveryKeyMaterial Key => SharedKey.Value;

    private static byte[] RandomBytes(int length)
    {
        var data = new byte[length];
        RandomNumberGenerator.Fill(data);
        return data;
    }

    private static async Task<byte[]> EncryptAsync(byte[] plain, string? publicKey = null)
    {
        using var input = new MemoryStream(plain);
        using var output = new MemoryStream();
        await BackupCrypto.EncryptAsync(input, output, publicKey ?? Key.PublicKeyPem);
        return output.ToArray();
    }

    private static async Task<byte[]> DecryptAsync(byte[] cipher, string? privateKey = null, string passphrase = Passphrase)
    {
        using var input = new MemoryStream(cipher);
        using var output = new MemoryStream();
        await BackupCrypto.DecryptAsync(input, output, privateKey ?? Key.EncryptedPrivateKeyPem, passphrase);
        return output.ToArray();
    }

    private static async Task<string> DecryptCodeAsync(byte[] cipher, string? privateKey = null, string passphrase = Passphrase)
    {
        var ex = await Assert.ThrowsAsync<BackupCryptoException>(() => DecryptAsync(cipher, privateKey, passphrase));
        return ex.Code;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(BackupCrypto.ChunkSize - 1)]
    [InlineData(BackupCrypto.ChunkSize)]       // exactly one full chunk: the final-chunk boundary case
    [InlineData(BackupCrypto.ChunkSize + 1)]
    [InlineData(BackupCrypto.ChunkSize * 2)]   // exactly two full chunks
    [InlineData(BackupCrypto.ChunkSize * 2 + 12345)]
    public async Task Content_of_every_size_round_trips_exactly(int length)
    {
        var plain = RandomBytes(length);
        Assert.Equal(plain, await DecryptAsync(await EncryptAsync(plain)));
    }

    [Fact]
    public async Task The_ciphertext_does_not_contain_the_plaintext_and_differs_on_every_encryption()
    {
        var marker = Encoding.ASCII.GetBytes("PLAINTEXT-MARKER-PATIENT-DATA-0123456789");
        var plain = marker.Concat(new byte[1000]).ToArray();
        var first = await EncryptAsync(plain);
        var second = await EncryptAsync(plain);

        Assert.DoesNotContain(Encoding.ASCII.GetString(marker), Encoding.Latin1.GetString(first));
        Assert.NotEqual(first, second); // fresh data key and nonce each time
    }

    [Fact]
    public async Task A_wrong_passphrase_or_a_different_recovery_key_is_reported_as_wrong_recovery_material()
    {
        var cipher = await EncryptAsync(RandomBytes(5000));
        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, passphrase: "not the passphrase at all"));

        var other = BackupCrypto.GenerateRecoveryKey(Passphrase);
        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, other.EncryptedPrivateKeyPem));

        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, "this is not a key file at all"));
    }

    [Fact]
    public async Task The_server_side_public_key_alone_can_never_decrypt()
    {
        var cipher = await EncryptAsync(RandomBytes(5000));
        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, Key.PublicKeyPem)); // the only half the server stores
    }

    [Fact]
    public void The_recovery_key_pair_keeps_the_private_half_encrypted_and_the_public_half_free_of_it()
    {
        Assert.StartsWith("-----BEGIN ENCRYPTED PRIVATE KEY-----", Key.EncryptedPrivateKeyPem);
        Assert.DoesNotContain("PRIVATE", Key.PublicKeyPem);
        Assert.Throws<CryptographicException>(() => RSA.Create().ImportFromEncryptedPem(Key.EncryptedPrivateKeyPem, "wrong passphrase"));
        Assert.Equal(64, Key.Fingerprint.Length);
        Assert.Equal(Key.Fingerprint, BackupCrypto.FingerprintOfPublicKeyPem(Key.PublicKeyPem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("elevenchars")]
    public void A_weak_recovery_passphrase_is_rejected(string passphrase)
    {
        var ex = Assert.Throws<BackupCryptoException>(() => BackupCrypto.GenerateRecoveryKey(passphrase));
        Assert.Equal("weak_passphrase", ex.Code);
    }

    [Fact]
    public async Task Altering_any_part_of_the_file_is_detected()
    {
        var plain = RandomBytes(BackupCrypto.ChunkSize * 2 + 5000);
        var cipher = await EncryptAsync(plain);

        // Header: the wrapped key block, the nonce prefix, the chunk size.
        foreach (var offset in new[] { 5 + 1 + 32 + 2 + 10, cipher.Length > 0 ? 5 + 1 + 32 + 2 + 384 + 2 : 0 })
        {
            var tampered = (byte[])cipher.Clone();
            tampered[offset] ^= 0x01;
            Assert.Contains(await DecryptCodeAsync(tampered), new[] { "corrupt_or_tampered", "wrong_recovery_key" });
        }

        // Body: a byte in the first chunk, in a middle chunk, in the final chunk, and in a tag.
        foreach (var offset in new[] { 5 + 1 + 32 + 2 + 384 + 8 + 4 + 4 + 100, cipher.Length / 2, cipher.Length - 20, cipher.Length - 1 })
        {
            var tampered = (byte[])cipher.Clone();
            tampered[offset] ^= 0x80;
            Assert.Equal("corrupt_or_tampered", await DecryptCodeAsync(tampered));
        }
    }

    [Fact]
    public async Task Truncation_at_any_point_including_a_chunk_boundary_is_detected()
    {
        var cipher = await EncryptAsync(RandomBytes(BackupCrypto.ChunkSize * 3 + 100));
        var firstChunkEnd = 5 + 1 + 32 + 2 + 384 + 8 + 4 + 4 + BackupCrypto.ChunkSize + 16;
        foreach (var length in new[] { cipher.Length - 1, cipher.Length - 16, firstChunkEnd, firstChunkEnd + 4, 10 })
            Assert.Contains(await DecryptCodeAsync(cipher[..length]), new[] { "corrupt_or_tampered", "not_a_backup" });
    }

    [Fact]
    public async Task Appending_data_after_the_final_chunk_is_detected()
    {
        var cipher = await EncryptAsync(RandomBytes(5000));
        Assert.Equal("corrupt_or_tampered", await DecryptCodeAsync(cipher.Concat(new byte[] { 0, 0, 0, 20 }).Concat(RandomBytes(20)).ToArray()));
        Assert.Equal("corrupt_or_tampered", await DecryptCodeAsync(cipher.Concat(new byte[] { 1 }).ToArray()));
    }

    [Fact]
    public async Task Swapping_two_chunks_is_detected()
    {
        var cipher = await EncryptAsync(RandomBytes(BackupCrypto.ChunkSize * 3 + 10));
        var headerLength = 5 + 1 + 32 + 2 + 384 + 8 + 4;
        var chunkTotal = 4 + BackupCrypto.ChunkSize + 16;
        var swapped = (byte[])cipher.Clone();
        Array.Copy(cipher, headerLength + chunkTotal, swapped, headerLength, chunkTotal);
        Array.Copy(cipher, headerLength, swapped, headerLength + chunkTotal, chunkTotal);
        Assert.Equal("corrupt_or_tampered", await DecryptCodeAsync(swapped));
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_or_is_a_newer_format_is_rejected_with_its_own_reason()
    {
        Assert.Equal("not_a_backup", await DecryptCodeAsync(Encoding.ASCII.GetBytes("This is just a text file, not an Alveara backup at all.")));
        Assert.Equal("not_a_backup", await DecryptCodeAsync([]));

        var cipher = await EncryptAsync(RandomBytes(100));
        var newer = (byte[])cipher.Clone();
        newer[5] = 2; // version byte
        Assert.Equal("unsupported_version", await DecryptCodeAsync(newer));
    }

    [Fact]
    public async Task The_header_fingerprint_identifies_the_key_without_any_secret()
    {
        var cipher = await EncryptAsync(RandomBytes(100));
        using var stream = new MemoryStream(cipher);
        Assert.Equal(Key.Fingerprint, await BackupCrypto.ReadFingerprintAsync(stream));
    }
}
