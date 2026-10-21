using Alveara.Api.Architecture.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// An API test host used to keep its files in one shared <c>Test-Data\Storage\app</c> folder that nothing ever emptied (900 files and growing). Each host now has its own storage folder, made when the host starts and removed when it is disposed;
/// a test that names its own folder keeps responsibility for it.
/// </summary>
public class ApiHarnessStorageCleanupTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_host_has_its_own_storage_folder_which_is_removed_with_it_and_the_shared_folder_is_left_alone()
    {
        var shared = Path.Combine(AlveraTestEnvironment.StorageFolder, "app");
        var sharedBefore = Directory.Exists(shared) ? Directory.GetFileSystemEntries(shared).Length : 0;
        var api = new SchedulingApiHarness(_fixture);
        await api.SessionAsync("Admin");
        _ = api.Factory.Services.GetRequiredService<IBlobStorage>();      // the first document operation makes the host create its storage folder

        var root = api.StorageRoot;
        Assert.NotNull(root);
        Assert.NotEqual(shared, root);
        Assert.StartsWith(AlveraTestEnvironment.StorageFolder, root);
        Assert.True(Directory.Exists(root));

        api.Dispose();

        Assert.False(Directory.Exists(root));
        Assert.Equal(sharedBefore, Directory.Exists(shared) ? Directory.GetFileSystemEntries(shared).Length : 0);
    }

    [Fact]
    public async Task A_folder_a_test_names_itself_is_not_removed_by_the_host()
    {
        var own = Path.Combine(AlveraTestEnvironment.StorageFolder, $"api-harness-own-{Guid.NewGuid():N}");
        try
        {
            var api = new SchedulingApiHarness(_fixture) { StorageRoot = own };
            await api.SessionAsync("Admin");
            _ = api.Factory.Services.GetRequiredService<IBlobStorage>();
            Assert.True(Directory.Exists(own));

            api.Dispose();

            Assert.True(Directory.Exists(own));                           // its owner removes it
        }
        finally { TestStorageCleanup.Delete(own); }
    }

    [Fact]
    public void Disposing_a_host_that_never_started_is_harmless()
    {
        var api = new SchedulingApiHarness(_fixture);
        api.Dispose();
        api.Dispose();
    }
}
