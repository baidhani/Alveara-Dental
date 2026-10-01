using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Bcpg.Sig;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Alveara.Api.Architecture.Backup;

/// <summary>A backup file could not be decrypted/authenticated. <see cref="Code"/> is stable and safe to show.</summary>
public sealed class BackupCryptoException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The recovery key pair for an installation: both halves as ASCII-armored OpenPGP key blocks, the
/// encryption key's fingerprint, and the generated passphrase protecting the private half.
/// <see cref="EncryptedPrivateKeyPem"/> and <see cref="Passphrase"/> are shown to the administrator once and never stored.
/// </summary>
public sealed record RecoveryKeyMaterial(string PublicKeyPem, string EncryptedPrivateKeyPem, string Fingerprint, string Passphrase = "");

/// <summary>
/// ALV-N004's encryption layer: <b>OpenPGP (RFC 4880 / RFC 9580), implemented by the maintained
/// BouncyCastle library</b>. A backup is an ordinary OpenPGP message - public-key encrypted
/// (RSA-3072) with AES-256, integrity-protected by the format's own Modification Detection Code - so
/// it is a documented, interoperable, authenticated container (any OpenPGP implementation, e.g.
/// GnuPG, decrypts it with the recovery key; the tests prove this against a real <c>gpg</c>). This
/// class defines NO cipher, key-derivation or container format of its own: key generation,
/// session-key wrapping, bulk encryption, packet framing, integrity checking and passphrase
/// protection of the private key are all the library's. What remains here is plumbing: choose the
/// recipient key, stream bytes through the library, and translate its failures into stable codes.
/// <para>
/// The server holds only the PUBLIC key: it can encrypt every unattended backup but can never
/// decrypt one. The private half exists only in the administrator's offline custody, protected by a
/// server-generated high-entropy passphrase (OpenPGP's own string-to-key derivation is deliberately
/// not relied on for strength, which is why the passphrase is generated, never chosen by a person).
/// </para>
/// <para>
/// Integrity: OpenPGP's MDC is verified over the whole stream before this class reports success, and
/// decrypted output is only ever written to scratch space that callers discard on failure - nothing
/// from an unauthenticated message is ever trusted or extracted.
/// </para>
/// </summary>
public static class BackupCrypto
{
    private const int BufferSize = 1 << 16;
    private const int MinimumPassphraseLength = 16;
    private const string KeyUserId = "Alveara backup recovery key";

    // ---------- key material ----------

    public static string GeneratePassphrase()
    {
        // 24 characters of RFC 4648 base32 = 120 bits from the platform CSPRNG, grouped for transcription.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var chars = new char[24];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return string.Join('-', Enumerable.Range(0, 6).Select(g => new string(chars, g * 4, 4)));
    }

    public static RecoveryKeyMaterial GenerateRecoveryKey(string? passphrase = null)
    {
        passphrase ??= GeneratePassphrase();
        if (passphrase.Length < MinimumPassphraseLength)
            throw new BackupCryptoException("weak_passphrase", $"The recovery key passphrase must be at least {MinimumPassphraseLength} characters.");

        var random = new SecureRandom();
        var generator = new RsaKeyPairGenerator();
        generator.Init(new RsaKeyGenerationParameters(BigInteger.ValueOf(0x10001), random, 3072, 80));
        var master = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, generator.GenerateKeyPair(), DateTime.UtcNow);
        var encryption = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, generator.GenerateKeyPair(), DateTime.UtcNow);

        var masterPackets = new PgpSignatureSubpacketGenerator();
        masterPackets.SetKeyFlags(false, PgpKeyFlags.CanCertify | PgpKeyFlags.CanSign);
        masterPackets.SetPreferredSymmetricAlgorithms(false, [(int)SymmetricKeyAlgorithmTag.Aes256]);
        masterPackets.SetPreferredHashAlgorithms(false, [(int)HashAlgorithmTag.Sha256]);
        var encryptionPackets = new PgpSignatureSubpacketGenerator();
        encryptionPackets.SetKeyFlags(false, PgpKeyFlags.CanEncryptCommunications | PgpKeyFlags.CanEncryptStorage);

        var ringGenerator = new PgpKeyRingGenerator(
            PgpSignature.PositiveCertification, master, KeyUserId, SymmetricKeyAlgorithmTag.Aes256, HashAlgorithmTag.Sha256,
            passphrase.ToCharArray(), true, masterPackets.Generate(), null, random);
        ringGenerator.AddSubKey(encryption, encryptionPackets.Generate(), null);

        var secretRing = ringGenerator.GenerateSecretKeyRing();
        var publicRing = ringGenerator.GeneratePublicKeyRing();
        var encryptionKey = publicRing.GetPublicKeys().Cast<PgpPublicKey>().Single(k => !k.IsMasterKey && k.IsEncryptionKey);
        return new RecoveryKeyMaterial(Armor(publicRing.Encode), Armor(secretRing.Encode), Convert.ToHexStringLower(encryptionKey.GetFingerprint()), passphrase);
    }

    private static string Armor(Action<Stream> write)
    {
        using var buffer = new MemoryStream();
        using (var armored = new ArmoredOutputStream(buffer)) write(armored);
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static PgpPublicKey EncryptionKeyOf(string publicKeyArmor)
    {
        try
        {
            using var stream = PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.ASCII.GetBytes(publicKeyArmor)));
            var rings = new PgpPublicKeyRingBundle(stream);
            return rings.GetKeyRings().Cast<PgpPublicKeyRing>().SelectMany(r => r.GetPublicKeys().Cast<PgpPublicKey>()).First(k => !k.IsMasterKey && k.IsEncryptionKey);
        }
        catch (Exception ex) when (ex is PgpException or IOException or InvalidOperationException or ArgumentException)
        {
            throw new BackupCryptoException("invalid_recovery_key", "The configured recovery public key is not valid.");
        }
    }

    public static string FingerprintOfPublicKeyPem(string publicKeyArmor) => Convert.ToHexStringLower(EncryptionKeyOf(publicKeyArmor).GetFingerprint());

    private static PgpSecretKeyRingBundle SecretKeysOf(string secretKeyArmor)
    {
        try
        {
            return new PgpSecretKeyRingBundle(PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.ASCII.GetBytes(secretKeyArmor ?? ""))));
        }
        catch (Exception ex) when (ex is PgpException or IOException or ArgumentException or InvalidCastException)
        {
            // A malformed key file and a wrong passphrase are deliberately indistinguishable to the caller.
            throw new BackupCryptoException("wrong_recovery_key", "The recovery key or its passphrase is wrong.");
        }
    }

    /// <summary>The id (upper-case hex) of the recovery key's ENCRYPTION key, readable from the key file without its passphrase.</summary>
    public static string EncryptionKeyIdOfSecretKey(string secretKeyArmor)
    {
        var secret = SecretKeysOf(secretKeyArmor).GetKeyRings().Cast<PgpSecretKeyRing>().SelectMany(r => r.GetSecretKeys().Cast<PgpSecretKey>()).FirstOrDefault(k => !k.IsMasterKey && k.PublicKey.IsEncryptionKey);
        return secret is null ? throw new BackupCryptoException("wrong_recovery_key", "The recovery key or its passphrase is wrong.") : secret.KeyId.ToString("X16");
    }

    /// <summary>Proves the passphrase unlocks the key file's encryption key (without decrypting anything).</summary>
    public static void EnsurePassphraseUnlocks(string secretKeyArmor, string passphrase)
    {
        var secret = SecretKeysOf(secretKeyArmor).GetKeyRings().Cast<PgpSecretKeyRing>().SelectMany(r => r.GetSecretKeys().Cast<PgpSecretKey>()).FirstOrDefault(k => !k.IsMasterKey && k.PublicKey.IsEncryptionKey)
            ?? throw new BackupCryptoException("wrong_recovery_key", "The recovery key or its passphrase is wrong.");
        try { secret.ExtractPrivateKey((passphrase ?? "").ToCharArray()); }
        catch (PgpException) { throw new BackupCryptoException("wrong_recovery_key", "The recovery key or its passphrase is wrong."); }
    }

    // ---------- encryption ----------

    /// <summary>Encrypts <paramref name="plaintext"/> to <paramref name="output"/> as an OpenPGP message for the holder of the recovery key. Streams in 64 KiB buffers (bounded memory for any size).</summary>
    public static Task EncryptAsync(Stream plaintext, Stream output, string recoveryPublicKeyArmor, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var key = EncryptionKeyOf(recoveryPublicKeyArmor);
            var generator = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Aes256, withIntegrityPacket: true, new SecureRandom());
            generator.AddMethod(key);
            using var encrypted = generator.Open(output, new byte[BufferSize]);
            using var literal = new PgpLiteralDataGenerator().Open(encrypted, PgpLiteralData.Binary, "backup.zip", DateTime.UtcNow, new byte[BufferSize]);
            var buffer = new byte[BufferSize];
            int read;
            while ((read = plaintext.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                literal.Write(buffer, 0, read);
            }
        }, cancellationToken);

    // ---------- reading / decryption ----------

    private static PgpEncryptedDataList ReadEncryptedList(PgpObjectFactory factory)
    {
        try
        {
            var first = factory.NextPgpObject();
            return first as PgpEncryptedDataList ?? factory.NextPgpObject() as PgpEncryptedDataList
                ?? throw new BackupCryptoException("not_a_backup", "This file is not an Alveara backup.");
        }
        catch (Exception ex) when (ex is IOException or PgpException or InvalidCastException or ArgumentException)
        {
            throw new BackupCryptoException("not_a_backup", "This file is not an Alveara backup.");
        }
    }

    /// <summary>The recipient key id (upper-case hex) a backup was encrypted for, read from its first packet without any secret.</summary>
    public static Task<string> ReadRecipientKeyIdAsync(Stream input, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var list = ReadEncryptedList(new PgpObjectFactory(PgpUtilities.GetDecoderStream(input)));
            var recipient = list.GetEncryptedDataObjects().Cast<PgpEncryptedData>().OfType<PgpPublicKeyEncryptedData>().FirstOrDefault()
                ?? throw new BackupCryptoException("not_a_backup", "This file is not an Alveara backup.");
            return recipient.KeyId.ToString("X16");
        }, cancellationToken);

    /// <summary>
    /// Decrypts and authenticates a whole backup into <paramref name="output"/>. A wrong key or passphrase, a
    /// corrupted/truncated/extended/tampered file and a non-backup file are distinguished by
    /// <see cref="BackupCryptoException.Code"/>. The output is only complete and trustworthy if this method returns.
    /// </summary>
    public static Task DecryptAsync(Stream input, Stream output, string encryptedPrivateKeyArmor, string passphrase, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var writes = new WriteFaultCapturingStream(output);
            try
            {
                var factory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(input));
                var list = ReadEncryptedList(factory);
                var secrets = SecretKeysOf(encryptedPrivateKeyArmor);

                PgpPublicKeyEncryptedData? message = null;
                PgpSecretKey? secretKey = null;
                foreach (var candidate in list.GetEncryptedDataObjects().Cast<PgpEncryptedData>().OfType<PgpPublicKeyEncryptedData>())
                {
                    secretKey = secrets.GetSecretKey(candidate.KeyId);
                    if (secretKey is not null) { message = candidate; break; }
                }
                if (message is null || secretKey is null)
                    throw new BackupCryptoException("wrong_recovery_key", "This recovery key does not belong to this backup.");

                PgpPrivateKey privateKey;
                try { privateKey = secretKey.ExtractPrivateKey((passphrase ?? "").ToCharArray()); }
                catch (PgpException) { throw new BackupCryptoException("wrong_recovery_key", "The recovery key or its passphrase is wrong."); }

                using var clear = message.GetDataStream(privateKey);
                var content = new PgpObjectFactory(clear);
                var item = content.NextPgpObject();
                if (item is PgpCompressedData compressed)
                    item = new PgpObjectFactory(compressed.GetDataStream()).NextPgpObject();
                var literal = item as PgpLiteralData ?? throw new BackupCryptoException("corrupt_or_tampered", "The backup contains no data.");

                var buffer = new byte[BufferSize];
                using (var data = literal.GetInputStream())
                {
                    int read;
                    while ((read = data.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writes.Write(buffer, 0, read);
                    }
                }

                if (!message.IsIntegrityProtected() || !message.Verify())
                    throw new BackupCryptoException("corrupt_or_tampered", "The backup failed authentication: it is corrupt or has been altered.");
                if (factory.NextPgpObject() is not null) // nothing may follow the one authenticated message
                    throw new BackupCryptoException("corrupt_or_tampered", "The backup has unexpected data after its end.");
            }
            catch (BackupCryptoException) { throw; }
            catch (Exception) when (writes.Fault is not null) { throw writes.Fault; } // a failing DESTINATION (e.g. disk full) is not a damaged backup
            catch (Exception ex) when (ex is PgpException or IOException or EndOfStreamException or InvalidCastException or ArgumentException or InvalidOperationException or FormatException)
            {
                throw new BackupCryptoException("corrupt_or_tampered", "The backup failed authentication: it is corrupt or has been altered.");
            }
        }, cancellationToken);

    /// <summary>Remembers a failure of the destination stream so it is reported as such, not as a corrupt backup.</summary>
    private sealed class WriteFaultCapturingStream(Stream inner)
    {
        public Exception? Fault { get; private set; }

        public void Write(byte[] buffer, int offset, int count)
        {
            try { inner.Write(buffer, offset, count); }
            catch (Exception ex) { Fault = ex; throw; }
        }
    }
}
