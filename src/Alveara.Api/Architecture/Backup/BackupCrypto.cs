using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Alveara.Api.Architecture.Backup;

/// <summary>A backup file could not be decrypted/authenticated. <see cref="Code"/> is stable and safe to show.</summary>
public sealed class BackupCryptoException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The recovery key pair for a freshly configured installation.</summary>
public sealed record RecoveryKeyMaterial(string PublicKeyPem, string EncryptedPrivateKeyPem, string Fingerprint);

/// <summary>
/// ALV-N004's encryption layer. Every primitive is a platform one - nothing here invents
/// cryptography:
/// <list type="bullet">
/// <item>RSA-3072 + OAEP-SHA256 (<see cref="RSA"/>) wraps a per-backup random 256-bit data key, so the server holds
///   only the PUBLIC half: it can encrypt every unattended backup but can never decrypt one, and the
///   recovery secret (the private key, itself exported with PBES2/PBKDF2-SHA256 600k + AES-256 via
///   <c>ExportEncryptedPkcs8PrivateKeyPem</c>) exists only in the administrator's custody - never in a
///   backup, the database, configuration, or a log.</item>
/// <item>AES-256-GCM (<see cref="AesGcm"/>) provides authenticated encryption of the content.</item>
/// </list>
/// The only thing implemented here is the minimal framing that applies AES-GCM to a large stream:
/// the standard "STREAM" chunking construction (fixed-size chunks, per-chunk nonce = random prefix ||
/// counter, additional-authenticated-data binding the header, chunk index, length and a final-chunk
/// flag), so reordering, truncation, extension or header tampering all fail authentication.
/// File layout: magic "ALVBK" | version | key fingerprint (32) | wrapped-key length (2) | wrapped key |
/// nonce prefix (8) | chunk size (4) | then repeated: ciphertext length (4) | ciphertext+tag.
/// </summary>
public static class BackupCrypto
{
    private static readonly byte[] Magic = "ALVBK"u8.ToArray();
    private const byte FormatVersion = 1;
    public const int ChunkSize = 1 << 20; // 1 MiB of plaintext per chunk
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int NoncePrefixSize = 8;
    private const int Pbkdf2Iterations = 600_000;

    public static RecoveryKeyMaterial GenerateRecoveryKey(string passphrase)
    {
        if (string.IsNullOrEmpty(passphrase) || passphrase.Length < 12)
            throw new BackupCryptoException("weak_passphrase", "The recovery key passphrase must be at least 12 characters.");

        using var rsa = RSA.Create(3072);
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var encryptedPrivate = rsa.ExportEncryptedPkcs8PrivateKeyPem(
            passphrase, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, Pbkdf2Iterations));
        return new RecoveryKeyMaterial(rsa.ExportSubjectPublicKeyInfoPem(), encryptedPrivate, Fingerprint(spki));
    }

    public static string FingerprintOfPublicKeyPem(string publicKeyPem)
    {
        using var rsa = ImportPublic(publicKeyPem);
        return Fingerprint(rsa.ExportSubjectPublicKeyInfo());
    }

    private static string Fingerprint(byte[] spki) => Convert.ToHexStringLower(SHA256.HashData(spki));

    private static RSA ImportPublic(string publicKeyPem)
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicKeyPem);
            return rsa;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            rsa.Dispose();
            throw new BackupCryptoException("invalid_recovery_key", "The configured recovery public key is not valid.");
        }
    }

    /// <summary>Encrypts <paramref name="plaintext"/> to <paramref name="output"/> for the holder of the recovery key.</summary>
    public static async Task EncryptAsync(Stream plaintext, Stream output, string recoveryPublicKeyPem, CancellationToken cancellationToken = default)
    {
        using var rsa = ImportPublic(recoveryPublicKeyPem);
        var fingerprint = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());
        var dek = RandomNumberGenerator.GetBytes(32);
        var wrapped = rsa.Encrypt(dek, RSAEncryptionPadding.OaepSHA256);
        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);

        var header = new byte[Magic.Length + 1 + 32 + 2 + wrapped.Length + NoncePrefixSize + 4];
        var span = header.AsSpan();
        Magic.CopyTo(span); span = span[Magic.Length..];
        span[0] = FormatVersion; span = span[1..];
        fingerprint.CopyTo(span); span = span[32..];
        BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)wrapped.Length); span = span[2..];
        wrapped.CopyTo(span); span = span[wrapped.Length..];
        noncePrefix.CopyTo(span); span = span[NoncePrefixSize..];
        BinaryPrimitives.WriteInt32BigEndian(span, ChunkSize);
        await output.WriteAsync(header, cancellationToken);

        var headerHash = SHA256.HashData(header);
        using var aes = new AesGcm(dek, TagSize);

        var current = new byte[ChunkSize];
        var next = new byte[ChunkSize];
        var currentLength = await ReadFullAsync(plaintext, current, cancellationToken);
        uint counter = 0;
        while (true)
        {
            var nextLength = currentLength == ChunkSize ? await ReadFullAsync(plaintext, next, cancellationToken) : 0;
            var isFinal = nextLength == 0;
            await WriteChunkAsync(aes, noncePrefix, headerHash, counter, isFinal, current.AsMemory(0, currentLength), output, cancellationToken);
            if (isFinal) break;
            (current, next) = (next, current);
            currentLength = nextLength;
            counter++;
        }

        CryptographicOperations.ZeroMemory(dek);
    }

    private static async Task WriteChunkAsync(AesGcm aes, byte[] noncePrefix, byte[] headerHash, uint counter, bool isFinal,
        ReadOnlyMemory<byte> plain, Stream output, CancellationToken cancellationToken)
    {
        var nonce = new byte[NonceSize];
        noncePrefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixSize), counter);

        var cipherLength = plain.Length + TagSize;
        var aad = BuildAad(headerHash, counter, isFinal, cipherLength);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        aes.Encrypt(nonce, plain.Span, cipher, tag, aad);

        var lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, cipherLength);
        await output.WriteAsync(lengthPrefix, cancellationToken);
        await output.WriteAsync(cipher, cancellationToken);
        await output.WriteAsync(tag, cancellationToken);
    }

    private static byte[] BuildAad(byte[] headerHash, uint counter, bool isFinal, int cipherLength)
    {
        var aad = new byte[32 + 4 + 1 + 4];
        headerHash.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(32), counter);
        aad[36] = isFinal ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(37), cipherLength);
        return aad;
    }

    /// <summary>The recovery-key fingerprint a backup was encrypted for, read from its header without any key.</summary>
    public static async Task<string> ReadFingerprintAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var (_, fingerprint, _, _, _) = await ReadHeaderAsync(input, cancellationToken);
        return Convert.ToHexStringLower(fingerprint);
    }

    private static async Task<(byte[] Header, byte[] Fingerprint, byte[] Wrapped, byte[] NoncePrefix, int ChunkSize)> ReadHeaderAsync(Stream input, CancellationToken cancellationToken)
    {
        var fixedPart = new byte[Magic.Length + 1 + 32 + 2];
        if (await ReadFullAsync(input, fixedPart, cancellationToken) != fixedPart.Length || !fixedPart.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new BackupCryptoException("not_a_backup", "This file is not an Alveara backup.");
        if (fixedPart[Magic.Length] != FormatVersion)
            throw new BackupCryptoException("unsupported_version", "This backup was written in a format this version cannot read.");

        var fingerprint = fixedPart.AsSpan(Magic.Length + 1, 32).ToArray();
        var wrappedLength = BinaryPrimitives.ReadUInt16BigEndian(fixedPart.AsSpan(Magic.Length + 1 + 32));
        if (wrappedLength is 0 or > 1024)
            throw new BackupCryptoException("corrupt_or_tampered", "The backup header is damaged.");

        var rest = new byte[wrappedLength + NoncePrefixSize + 4];
        if (await ReadFullAsync(input, rest, cancellationToken) != rest.Length)
            throw new BackupCryptoException("corrupt_or_tampered", "The backup header is truncated.");

        var wrapped = rest.AsSpan(0, wrappedLength).ToArray();
        var noncePrefix = rest.AsSpan(wrappedLength, NoncePrefixSize).ToArray();
        var chunkSize = BinaryPrimitives.ReadInt32BigEndian(rest.AsSpan(wrappedLength + NoncePrefixSize));
        if (chunkSize != ChunkSize)
            throw new BackupCryptoException("corrupt_or_tampered", "The backup header is damaged.");

        var header = new byte[fixedPart.Length + rest.Length];
        fixedPart.CopyTo(header, 0);
        rest.CopyTo(header, fixedPart.Length);
        return (header, fingerprint, wrapped, noncePrefix, chunkSize);
    }

    /// <summary>
    /// Decrypts and authenticates a whole backup. A wrong key, a corrupted/truncated/extended file and a
    /// non-backup file are distinguished by <see cref="BackupCryptoException.Code"/>; nothing is written
    /// to <paramref name="output"/> for a chunk that fails authentication.
    /// </summary>
    public static async Task DecryptAsync(Stream input, Stream output, string encryptedPrivateKeyPem, string passphrase, CancellationToken cancellationToken = default)
    {
        var (header, fingerprint, wrapped, noncePrefix, _) = await ReadHeaderAsync(input, cancellationToken);

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromEncryptedPem(encryptedPrivateKeyPem, passphrase);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            // Wrong passphrase and a malformed key file are deliberately indistinguishable here.
            throw new BackupCryptoException("wrong_recovery_key", "The recovery key or its passphrase is wrong.");
        }
        if (!SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()).AsSpan().SequenceEqual(fingerprint))
            throw new BackupCryptoException("wrong_recovery_key", "This recovery key does not belong to this backup.");

        byte[] dek;
        try
        {
            dek = rsa.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA256);
        }
        catch (CryptographicException)
        {
            throw new BackupCryptoException("corrupt_or_tampered", "The backup's key block could not be unwrapped.");
        }

        try
        {
            var headerHash = SHA256.HashData(header);
            using var aes = new AesGcm(dek, TagSize);
            uint counter = 0;
            var lengthBuffer = new byte[4];
            while (true)
            {
                var read = await ReadFullAsync(input, lengthBuffer, cancellationToken);
                if (read != 4) throw new BackupCryptoException("corrupt_or_tampered", "The backup is truncated.");
                var cipherLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
                if (cipherLength < TagSize || cipherLength > ChunkSize + TagSize)
                    throw new BackupCryptoException("corrupt_or_tampered", "The backup is damaged.");

                var buffer = new byte[cipherLength];
                if (await ReadFullAsync(input, buffer, cancellationToken) != cipherLength)
                    throw new BackupCryptoException("corrupt_or_tampered", "The backup is truncated.");

                var plain = new byte[cipherLength - TagSize];
                var nonce = new byte[NonceSize];
                noncePrefix.CopyTo(nonce, 0);
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixSize), counter);

                // Is this the final chunk? Try "final" first only if nothing follows; otherwise it must authenticate as non-final.
                var atEnd = input.CanSeek ? input.Position == input.Length : await PeekIsEndAsync(input, cancellationToken);
                var aad = BuildAad(headerHash, counter, atEnd, cipherLength);
                try
                {
                    aes.Decrypt(nonce, buffer.AsSpan(0, plain.Length), buffer.AsSpan(plain.Length, TagSize), plain, aad);
                }
                catch (CryptographicException)
                {
                    throw new BackupCryptoException("corrupt_or_tampered", "The backup failed authentication: it is corrupt or has been altered.");
                }

                await output.WriteAsync(plain, cancellationToken);
                if (atEnd) return;
                counter++;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    private static async Task<bool> PeekIsEndAsync(Stream input, CancellationToken cancellationToken)
    {
        // Non-seekable fallback is not used by this application (backups are files); fail closed.
        await Task.CompletedTask;
        throw new BackupCryptoException("corrupt_or_tampered", "Backups must be read from a seekable file.");
    }

    private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
