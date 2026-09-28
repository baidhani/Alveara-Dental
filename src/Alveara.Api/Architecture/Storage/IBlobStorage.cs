using System.Security.Cryptography;

namespace Alveara.Api.Architecture.Storage;

/// <summary>
/// Storage-root/blob abstraction seam for ALV-N002. This story does not build the document
/// module (that's ALV-N010/ALV-010-C01) — it only establishes where a blob lives and how its
/// integrity is verified, so those later stories don't have to invent this from scratch, and so
/// stored file identity never depends on a user-supplied filename (per the Engineering Reference's
/// files/documents rule).
/// </summary>
public interface IBlobStorage
{
    /// <summary>
    /// Stores content under a storage-generated identity (never the caller's filename) and
    /// returns that identity plus a SHA-256 integrity hash for later corruption detection.
    /// </summary>
    Task<StoredBlob> StoreAsync(Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(Guid blobId, CancellationToken cancellationToken = default);

    /// <summary>Recomputes the hash of the stored content and compares it to the recorded one.</summary>
    Task<bool> VerifyIntegrityAsync(Guid blobId, string expectedSha256, CancellationToken cancellationToken = default);
}

public sealed record StoredBlob(Guid Id, string Sha256Hash, long SizeBytes);

/// <summary>
/// Local-disk implementation for first release (single Windows server, no cloud storage yet).
/// Files are named by their generated Guid, never by caller-supplied names, under a configured
/// storage root.
/// </summary>
public sealed class LocalDiskBlobStorage : IBlobStorage
{
    private readonly string _storageRoot;

    public LocalDiskBlobStorage(string storageRoot)
    {
        _storageRoot = storageRoot;
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<StoredBlob> StoreAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var path = PathFor(id);

        using var sha256 = SHA256.Create();
        await using (var fileStream = File.Create(path))
        await using (var hashingStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write, leaveOpen: false))
        {
            await content.CopyToAsync(hashingStream, cancellationToken);
        }

        var hash = Convert.ToHexStringLower(sha256.Hash!);
        var size = new FileInfo(path).Length;
        return new StoredBlob(id, hash, size);
    }

    public Task<Stream> OpenReadAsync(Guid blobId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(blobId);
        if (!File.Exists(path)) throw new FileNotFoundException($"Blob {blobId} not found in storage.", path);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public async Task<bool> VerifyIntegrityAsync(Guid blobId, string expectedSha256, CancellationToken cancellationToken = default)
    {
        var path = PathFor(blobId);
        if (!File.Exists(path)) return false;

        using var sha256 = SHA256.Create();
        await using var fileStream = File.OpenRead(path);
        var hash = await sha256.ComputeHashAsync(fileStream, cancellationToken);
        var actual = Convert.ToHexStringLower(hash);
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private string PathFor(Guid blobId) => Path.Combine(_storageRoot, blobId.ToString("N"));
}
