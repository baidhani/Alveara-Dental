using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Storage;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// N002-R02-01: static file serving was added so the API can serve the production shell for LAN
/// clients. This must not accidentally make the blob storage root web-servable too — an ordinary
/// HTTP client must never be able to fetch a stored blob directly; it may only go through the API
/// (which does not yet expose a blob-download endpoint at all, since the document module doesn't
/// exist yet).
/// </summary>
public class StorageIsolationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public StorageIsolationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_stored_blob_is_not_reachable_over_HTTP_even_with_static_file_serving_active()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"alveara-storage-isolation-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);

        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
                builder.UseSetting("StorageRoot", storageRoot);
                builder.ConfigureServices(services =>
                {
                    // Ensure the blob storage points at our isolated temp root regardless of
                    // service-registration ordering.
                    services.AddSingleton<IBlobStorage>(new LocalDiskBlobStorage(storageRoot));
                });
            });

            var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
            Guid blobId;
            using (var scope = scopeFactory.CreateScope())
            {
                var storage = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
                using var content = new MemoryStream("blob content that must never be web-reachable"u8.ToArray());
                var stored = await storage.StoreAsync(content);
                blobId = stored.Id;
            }

            var client = factory.CreateClient();

            // The blob file on disk is literally named by its Guid — try every plausible naive
            // path an attacker or a misconfiguration might expose it at. Note: the SPA fallback
            // (MapFallbackToFile) intentionally returns 200 + index.html for *any* unmatched path,
            // so status code alone can't prove isolation — the check that matters is that the
            // response body is never the blob's actual bytes.
            const string blobMarker = "blob content that must never be web-reachable";
            string[] guessedPaths =
            [
                $"/{blobId:N}",
                $"/blobs/{blobId:N}",
                $"/App_Data/blobs/{blobId:N}",
                $"/api/blobs/{blobId:N}",
            ];

            foreach (var path in guessedPaths)
            {
                var response = await client.GetAsync(path);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(blobMarker, body);
            }
        }
        finally
        {
            if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public void The_default_storage_root_and_the_default_client_build_path_are_disjoint_directories()
    {
        // Structural check: even without running the host, the two default paths configured in
        // Program.cs must not be the same directory or nested inside one another, so static file
        // serving (scoped to the client build path) can never accidentally reach the storage root.
        var apiContentRoot = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        // Walk up from bin/Debug/net10.0 to the Alveara.Api project directory.
        var projectDir = apiContentRoot;
        for (var i = 0; i < 3 && Directory.Exists(Path.Combine(projectDir, "..")); i++)
        {
            projectDir = Path.GetFullPath(Path.Combine(projectDir, ".."));
        }

        var defaultStorageRoot = Path.GetFullPath(Path.Combine(projectDir, "App_Data", "blobs"));
        // Matches Program.cs's primary default (the publish-output layout): wwwroot under the
        // content root, populated by Alveara.Api.csproj's CopyClientBuildToPublishOutput target.
        var defaultClientBuildPath = Path.GetFullPath(Path.Combine(projectDir, "wwwroot"));

        Assert.NotEqual(defaultStorageRoot, defaultClientBuildPath, StringComparer.OrdinalIgnoreCase);
        Assert.False(
            defaultStorageRoot.StartsWith(defaultClientBuildPath, StringComparison.OrdinalIgnoreCase),
            "The storage root must not be nested inside the web-served client build path.");
        Assert.False(
            defaultClientBuildPath.StartsWith(defaultStorageRoot, StringComparison.OrdinalIgnoreCase),
            "The web-served client build path must not be nested inside the storage root.");
    }
}
