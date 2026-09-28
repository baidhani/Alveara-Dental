using System.Text;
using Alveara.Api.Architecture.Storage;
using Xunit;

namespace Alveara.Api.Tests;

public class BlobStorageTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"alveara-blob-test-{Guid.NewGuid():N}");
    private readonly LocalDiskBlobStorage _storage;

    public BlobStorageTests()
    {
        _storage = new LocalDiskBlobStorage(_storageRoot);
    }

    [Fact]
    public async Task Stored_blob_identity_is_a_generated_guid_never_the_caller_s_filename()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello world"));
        var stored = await _storage.StoreAsync(content);

        Assert.NotEqual(Guid.Empty, stored.Id);
        // Nothing in the public API even accepts a filename parameter — this is structural, not
        // just behavioral.
    }

    [Fact]
    public async Task Verify_integrity_succeeds_for_unmodified_content_and_fails_for_corrupted_content()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("integrity check content"));
        var stored = await _storage.StoreAsync(content);

        var intactCheck = await _storage.VerifyIntegrityAsync(stored.Id, stored.Sha256Hash);
        Assert.True(intactCheck);

        // Simulate corruption: append a byte directly to the stored file on disk.
        var storedPath = Path.Combine(_storageRoot, stored.Id.ToString("N"));
        await File.AppendAllTextAsync(storedPath, "corruption");

        var corruptedCheck = await _storage.VerifyIntegrityAsync(stored.Id, stored.Sha256Hash);
        Assert.False(corruptedCheck);
    }

    [Fact]
    public async Task Stored_content_can_be_read_back_exactly()
    {
        var original = "round trip content"u8.ToArray();
        using var content = new MemoryStream(original);
        var stored = await _storage.StoreAsync(content);

        await using var readBack = await _storage.OpenReadAsync(stored.Id);
        using var ms = new MemoryStream();
        await readBack.CopyToAsync(ms);

        Assert.Equal(original, ms.ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }
}
