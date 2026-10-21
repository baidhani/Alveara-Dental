using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01: the document tests write real files under the test data root. A test that leaves its storage folder behind makes the leak counts of a full run grow (3,500 folders had piled up from STORY-010 alone),
/// so the folder of every document test is removed with its database, and the removal reports a failure instead of hiding it.
/// </summary>
public class DocumentTestStorageCleanupTests
{
    private sealed class Probe : DocumentTestBase
    {
        public string Root => _storageRoot;
        public Task<Alveara.Api.Architecture.Storage.StoredBlob> StoreAsync() => _blobs.StoreAsync(new MemoryStream([1, 2, 3, 4]));
    }

    [Fact]
    public async Task A_document_tests_storage_folder_is_removed_when_the_test_ends()
    {
        var probe = new Probe();
        await probe.InitializeAsync();
        await probe.StoreAsync();
        Assert.True(Directory.Exists(probe.Root) && Directory.GetFiles(probe.Root).Length == 1);

        await probe.DisposeAsync();

        Assert.False(Directory.Exists(probe.Root));
    }

    [Fact]
    public async Task Disposing_twice_or_with_nothing_written_is_harmless()
    {
        var probe = new Probe();
        await probe.InitializeAsync();
        await probe.DisposeAsync();                                   // the folder was never created
        TestStorageCleanup.Delete(probe.Root);                        // and a second removal finds nothing to do
        Assert.False(Directory.Exists(probe.Root));
    }

    [Fact]
    public void A_folder_that_cannot_be_removed_is_reported_not_hidden()
    {
        var folder = Path.Combine(AlveraTestEnvironment.StorageFolder, $"cleanup-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var held = Path.Combine(folder, "held.bin");
        using (var open = new FileStream(held, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.Throws<IOException>(() => TestStorageCleanup.Delete(folder, attempts: 2, pause: TimeSpan.FromMilliseconds(10)));
            Assert.Contains("could not be removed", ex.Message);
        }
        TestStorageCleanup.Delete(folder);                            // once the file is released the same call succeeds
        Assert.False(Directory.Exists(folder));
    }
}
