using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Alveara.Api.Architecture.Backup;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N004 encryption/authentication/recovery-material tests. The container is a standard OpenPGP
/// message produced by BouncyCastle (no application-defined format), so these tests prove the
/// properties the story requires AND that the format is genuinely the documented one: a real GnuPG
/// decrypts what we write, and we decrypt what GnuPG writes.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class BackupCryptoTests
{
    private const string Passphrase = "correct horse battery staple 42";

    // Generating RSA-3072 key pairs is slow-ish, so share one pair across the tests in this class.
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

    // ---------- round trips ----------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(1_000_003)]
    [InlineData(5 * 1024 * 1024 + 17)]
    public async Task Content_of_every_size_round_trips_exactly(int length)
    {
        var plain = RandomBytes(length);
        Assert.Equal(plain, await DecryptAsync(await EncryptAsync(plain)));
    }

    [Fact]
    public async Task A_large_multi_chunk_file_streams_through_encryption_and_decryption_intact_and_tampering_deep_inside_it_is_detected()
    {
        // 96 MiB generated as a stream (never held in memory by the test), through real files: the realistic large-backup path.
        var dir = Path.Combine(Path.GetTempPath(), $"alv-crypto-large-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var plainPath = Path.Combine(dir, "plain.bin");
            var cipherPath = Path.Combine(dir, "cipher.abk");
            var outPath = Path.Combine(dir, "out.bin");
            using (var sha = SHA256.Create())
            {
                await using var plainFile = File.Create(plainPath);
                var block = new byte[1 << 20];
                for (var i = 0; i < 96; i++) { RandomNumberGenerator.Fill(block); plainFile.Write(block); }
            }
            var plainHash = Convert.ToHexString(SHA256.HashData(File.OpenRead(plainPath)));

            await using (var input = File.OpenRead(plainPath))
            await using (var output = File.Create(cipherPath))
                await BackupCrypto.EncryptAsync(input, output, Key.PublicKeyPem);
            Assert.True(new FileInfo(cipherPath).Length > 96L * 1024 * 1024);

            await using (var input = File.OpenRead(cipherPath))
            await using (var output = File.Create(outPath))
                await BackupCrypto.DecryptAsync(input, output, Key.EncryptedPrivateKeyPem, Passphrase);
            Assert.Equal(plainHash, Convert.ToHexString(SHA256.HashData(File.OpenRead(outPath))));

            // One flipped bit deep inside a 96 MiB message is still caught (the whole message is authenticated).
            await using (var tamper = new FileStream(cipherPath, FileMode.Open, FileAccess.ReadWrite))
            {
                tamper.Position = 60L * 1024 * 1024;
                var b = tamper.ReadByte();
                tamper.Position = 60L * 1024 * 1024;
                tamper.WriteByte((byte)(b ^ 0x01));
            }
            await using var tampered = File.OpenRead(cipherPath);
            await using var sink = new MemoryStream();
            var ex = await Assert.ThrowsAsync<BackupCryptoException>(() => BackupCrypto.DecryptAsync(tampered, Stream.Null, Key.EncryptedPrivateKeyPem, Passphrase));
            Assert.Equal("corrupt_or_tampered", ex.Code);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task The_ciphertext_does_not_contain_the_plaintext_and_differs_on_every_encryption()
    {
        var marker = Encoding.ASCII.GetBytes("PLAINTEXT-MARKER-PATIENT-DATA-0123456789");
        var plain = marker.Concat(new byte[1000]).ToArray();
        var first = await EncryptAsync(plain);
        var second = await EncryptAsync(plain);

        Assert.DoesNotContain(Encoding.ASCII.GetString(marker), Encoding.Latin1.GetString(first), StringComparison.Ordinal);
        Assert.NotEqual(first, second); // fresh session key and IV each time
    }

    // ---------- the format is standard OpenPGP, not ours ----------

    [Fact]
    public async Task The_backup_is_a_standard_OpenPGP_message_encrypted_with_AES_256_and_integrity_protection_for_the_recovery_key()
    {
        var cipher = await EncryptAsync(RandomBytes(1000));

        using var stream = PgpUtilities.GetDecoderStream(new MemoryStream(cipher));
        var factory = new PgpObjectFactory(stream);
        var list = Assert.IsType<PgpEncryptedDataList>(factory.NextPgpObject());
        var recipient = Assert.IsAssignableFrom<PgpPublicKeyEncryptedData>(Assert.Single(list.GetEncryptedDataObjects().Cast<PgpEncryptedData>()));
        Assert.Equal(BackupCrypto.EncryptionKeyIdOfSecretKey(Key.EncryptedPrivateKeyPem), recipient.KeyId.ToString("X16"));
        Assert.True(recipient.IsIntegrityProtected()); // the SEIPD packet (MDC) - the format's own authentication
        Assert.DoesNotContain("ALVBK", Encoding.Latin1.GetString(cipher), StringComparison.Ordinal); // no application-defined magic
    }

    [Fact]
    public void The_recovery_key_is_a_standard_armored_OpenPGP_key_pair_with_a_separate_encryption_key()
    {
        Assert.StartsWith("-----BEGIN PGP PRIVATE KEY BLOCK-----", Key.EncryptedPrivateKeyPem, StringComparison.Ordinal);
        Assert.StartsWith("-----BEGIN PGP PUBLIC KEY BLOCK-----", Key.PublicKeyPem, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", Key.PublicKeyPem, StringComparison.Ordinal);
        Assert.Equal(40, Key.Fingerprint.Length); // OpenPGP v4 fingerprint of the ENCRYPTION key
        Assert.Equal(Key.Fingerprint, BackupCrypto.FingerprintOfPublicKeyPem(Key.PublicKeyPem));
        Assert.EndsWith(Key.Fingerprint[^16..].ToUpperInvariant(), BackupCrypto.EncryptionKeyIdOfSecretKey(Key.EncryptedPrivateKeyPem), StringComparison.Ordinal);
    }

    // ---------- interoperability with an independent implementation (GnuPG) ----------

    private static string? FindGpg()
    {
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .SelectMany(d => new[] { Path.Combine(d, "gpg.exe"), Path.Combine(d, "gpg") }).Where(File.Exists);
        foreach (var candidate in onPath.Concat(new[] { @"C:\Program Files\Git\usr\bin\gpg.exe", @"C:\Program Files (x86)\GnuPG\bin\gpg.exe", "/usr/bin/gpg" }))
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(candidate, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
                p.WaitForExit(10_000);
                if (p.ExitCode == 0) return candidate;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* try next */ }
        }
        return null;
    }

    /// <summary>MSYS builds of gpg (Git for Windows) want /c/Users/... paths; native Windows builds want C:/Users/... Both accept forward slashes.</summary>
    private static string GpgPath(string gpg, string windowsPath)
    {
        var forward = windowsPath.Replace("\\", "/");
        var isMsys = gpg.Contains("usr", StringComparison.OrdinalIgnoreCase) && forward.Length > 2 && forward[1] == ':';
        return isMsys ? $"/{char.ToLowerInvariant(forward[0])}{forward[2..]}" : forward;
    }

    private static (int ExitCode, string Output) RunGpg(string gpg, string home, string args, string? stdin = null, string? stdoutFile = null)
    {
        var psi = new ProcessStartInfo(gpg, args) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.Environment["GNUPGHOME"] = GpgPath(gpg, home); // forward slashes work for both native and MSYS (Git for Windows) builds of gpg
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        if (stdoutFile is null) { var o = p.StandardOutput.ReadToEndAsync(); if (stdin is not null) p.StandardInput.Write(stdin); p.StandardInput.Close(); p.WaitForExit(120_000); return (p.ExitCode, o.Result + err.Result); }
        using (var f = File.Create(stdoutFile)) { var copy = p.StandardOutput.BaseStream.CopyToAsync(f); if (stdin is not null) p.StandardInput.Write(stdin); p.StandardInput.Close(); p.WaitForExit(120_000); copy.Wait(); }
        return (p.ExitCode, err.Result);
    }

    [Fact]
    public async Task A_real_GnuPG_decrypts_our_backup_with_the_recovery_key_and_we_decrypt_what_GnuPG_encrypts_to_our_key()
    {
        var gpg = FindGpg();
        if (gpg is null)
        {
            Assert.Fail("GnuPG was not found on this machine, so the OpenPGP interoperability proof could not run. Install gpg (Git for Windows ships it) to run this test.");
            return;
        }

        var home = Path.Combine(Path.GetTempPath(), $"alv-gpg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        try
        {
            var payload = RandomBytes(3 * 1024 * 1024 + 123);
            var cipher = await EncryptAsync(payload);
            var secretFile = Path.Combine(home, "recovery.asc");
            var cipherFile = Path.Combine(home, "backup.abk");
            var outFile = Path.Combine(home, "out.bin");
            await File.WriteAllTextAsync(secretFile, Key.EncryptedPrivateKeyPem);
            await File.WriteAllBytesAsync(cipherFile, cipher);

            // 1. GnuPG imports the recovery key file (with its passphrase) and decrypts OUR backup.
            var import = RunGpg(gpg, home, $"--batch --pinentry-mode loopback --passphrase \"{Passphrase}\" --import \"{GpgPath(gpg, secretFile)}\"");
            Assert.True(import.ExitCode == 0, import.Output);
            var decrypt = RunGpg(gpg, home, $"--batch --pinentry-mode loopback --passphrase \"{Passphrase}\" --decrypt \"{GpgPath(gpg, cipherFile)}\"", stdoutFile: outFile);
            Assert.True(decrypt.ExitCode == 0, decrypt.Output);
            Assert.Equal(payload, await File.ReadAllBytesAsync(outFile));

            // 2. GnuPG refuses the same file when one bit is altered (its own integrity check, not ours).
            var altered = (byte[])cipher.Clone();
            altered[altered.Length / 2] ^= 0x01;
            await File.WriteAllBytesAsync(cipherFile, altered);
            var refused = RunGpg(gpg, home, $"--batch --pinentry-mode loopback --passphrase \"{Passphrase}\" --decrypt \"{GpgPath(gpg, cipherFile)}\"", stdoutFile: outFile);
            Assert.NotEqual(0, refused.ExitCode);

            // 3. The other direction: GnuPG encrypts to our PUBLIC key; our code decrypts it.
            var publicFile = Path.Combine(home, "recovery.pub.asc");
            await File.WriteAllTextAsync(publicFile, Key.PublicKeyPem);
            Assert.Equal(0, RunGpg(gpg, home, $"--batch --import \"{GpgPath(gpg, publicFile)}\"").ExitCode);
            var plainFile = Path.Combine(home, "plain.bin");
            var encryptedByGpg = Path.Combine(home, "by-gpg.gpg");
            await File.WriteAllBytesAsync(plainFile, payload);
            var encrypt = RunGpg(gpg, home, $"--batch --yes --trust-model always --recipient {Key.Fingerprint} --output \"{GpgPath(gpg, encryptedByGpg)}\" --encrypt \"{GpgPath(gpg, plainFile)}\"");
            Assert.True(encrypt.ExitCode == 0, encrypt.Output);
            Assert.Equal(payload, await DecryptAsync(await File.ReadAllBytesAsync(encryptedByGpg)));
        }
        finally
        {
            RunGpg(gpg, home, "--batch --kill-agent"); // best effort: release the agent's files before cleanup
            try { Directory.Delete(home, recursive: true); } catch (IOException) { /* agent may still hold a file; temp */ }
        }
    }

    // ---------- wrong or missing recovery material ----------

    [Fact]
    public async Task A_wrong_passphrase_or_a_different_recovery_key_is_reported_as_wrong_recovery_material()
    {
        var cipher = await EncryptAsync(RandomBytes(5000));
        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, passphrase: "not the passphrase at all"));
        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, passphrase: ""));

        var other = BackupCrypto.GenerateRecoveryKey();
        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, other.EncryptedPrivateKeyPem, other.Passphrase)); // right passphrase, wrong key

        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, "this is not a key file at all"));
        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, ""));
    }

    [Fact]
    public async Task The_server_side_public_key_alone_can_never_decrypt()
    {
        var cipher = await EncryptAsync(RandomBytes(5000));
        Assert.Equal("wrong_recovery_key", await DecryptCodeAsync(cipher, Key.PublicKeyPem)); // the only half the server stores
    }

    [Fact]
    public void The_recovery_passphrase_is_generated_with_high_entropy_and_a_weak_chosen_one_is_rejected()
    {
        var one = BackupCrypto.GeneratePassphrase();
        var two = BackupCrypto.GeneratePassphrase();
        Assert.NotEqual(one, two);
        Assert.Matches("^([A-Z2-7]{4}-){5}[A-Z2-7]{4}$", one); // 24 base32 characters = 120 bits

        foreach (var weak in new[] { "", "short", "fifteen-chars!!" })
            Assert.Equal("weak_passphrase", Assert.Throws<BackupCryptoException>(() => BackupCrypto.GenerateRecoveryKey(weak)).Code);
    }

    // ---------- tamper / truncation / extension / splice ----------

    [Fact]
    public async Task Altering_any_part_of_the_file_is_detected_and_never_yields_plaintext()
    {
        var plain = RandomBytes(2 * 1024 * 1024 + 5000);
        var cipher = await EncryptAsync(plain);

        foreach (var offset in new[] { 5, 20, 400, 700, 4_000, cipher.Length / 4, cipher.Length / 2, cipher.Length - 100, cipher.Length - 30, cipher.Length - 1 })
        {
            var tampered = (byte[])cipher.Clone();
            tampered[offset] ^= 0x01;
            using var output = new MemoryStream();
            var ex = await Record.ExceptionAsync(() => BackupCrypto.DecryptAsync(new MemoryStream(tampered), output, Key.EncryptedPrivateKeyPem, Passphrase));
            var crypto = Assert.IsType<BackupCryptoException>(ex); // a flipped bit may never decrypt "successfully"
            Assert.Contains(crypto.Code, new[] { "corrupt_or_tampered", "wrong_recovery_key", "not_a_backup" });
        }
    }

    [Fact]
    public async Task Truncation_at_any_point_is_detected()
    {
        var cipher = await EncryptAsync(RandomBytes(300_000));
        foreach (var length in new[] { cipher.Length - 1, cipher.Length - 22, cipher.Length - 1000, cipher.Length / 2, 500, 10, 1 })
            Assert.Contains(await DecryptCodeAsync(cipher[..length]), new[] { "corrupt_or_tampered", "not_a_backup", "wrong_recovery_key" });
    }

    [Fact]
    public async Task Appending_data_after_the_message_is_detected()
    {
        var cipher = await EncryptAsync(RandomBytes(5000));
        Assert.Equal("corrupt_or_tampered", await DecryptCodeAsync(cipher.Concat(new byte[] { 1 }).ToArray()));
        Assert.Equal("corrupt_or_tampered", await DecryptCodeAsync(cipher.Concat(cipher).ToArray())); // a second valid message glued on
    }

    [Fact]
    public async Task Swapping_two_blocks_of_the_body_is_detected()
    {
        var cipher = await EncryptAsync(RandomBytes(1_000_000));
        var swapped = (byte[])cipher.Clone();
        Array.Copy(cipher, 100_000, swapped, 400_000, 65_536);
        Array.Copy(cipher, 400_000, swapped, 100_000, 65_536);
        Assert.Equal("corrupt_or_tampered", await DecryptCodeAsync(swapped));
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_is_rejected_with_its_own_reason()
    {
        Assert.Equal("not_a_backup", await DecryptCodeAsync(Encoding.ASCII.GetBytes("This is just a text file, not an Alveara backup at all.")));
        Assert.Equal("not_a_backup", await DecryptCodeAsync([]));
        Assert.Equal("not_a_backup", await DecryptCodeAsync(Encoding.ASCII.GetBytes("ALVBK").Concat(RandomBytes(500)).ToArray())); // the retired R01 container is not readable
    }

    [Fact]
    public async Task The_recipient_key_id_is_readable_from_the_file_without_any_secret()
    {
        var cipher = await EncryptAsync(RandomBytes(100));
        using var stream = new MemoryStream(cipher);
        Assert.Equal(BackupCrypto.EncryptionKeyIdOfSecretKey(Key.EncryptedPrivateKeyPem), await BackupCrypto.ReadRecipientKeyIdAsync(stream));
    }

    [Fact]
    public async Task A_failing_destination_is_reported_as_an_IO_failure_not_as_a_corrupt_backup()
    {
        var cipher = await EncryptAsync(RandomBytes(200_000));
        var ex = await Assert.ThrowsAsync<IOException>(() => BackupCrypto.DecryptAsync(new MemoryStream(cipher), new ThrowingStream(), Key.EncryptedPrivateKeyPem, Passphrase));
        Assert.Contains("disk", ex.Message);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
    }
}
