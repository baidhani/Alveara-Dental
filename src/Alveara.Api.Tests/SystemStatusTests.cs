using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Alveara.Api.Tests;

public class SystemStatusTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SystemStatusTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> FactoryWithConnectionString(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // UseSetting applies before the minimal-hosting Program.cs top-level code reads
            // builder.Configuration, unlike ConfigureAppConfiguration, whose ordering relative to
            // top-level statements is not guaranteed for the minimal hosting model.
            builder.UseSetting("ConnectionStrings:Alveara", connectionString);
        });

    [Fact]
    public async Task Reports_database_reachable_true_against_the_real_migrated_test_database()
    {
        await using var factory = FactoryWithConnectionString(_fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<SystemStatusResponse>("/api/systemstatus");

        Assert.NotNull(response);
        Assert.True(response.localServerReachable);
        Assert.True(response.database.reachable);
    }

    [Fact]
    public async Task Reports_database_reachable_false_without_crashing_when_the_database_is_unreachable()
    {
        // A syntactically valid but non-existent SQL Server instance — proves the endpoint
        // degrades gracefully (per the "Database unavailable" required failure path) rather than
        // throwing a 500.
        const string unreachableConnectionString =
            "Server=tcp:nonexistent-host-for-test,1433;Database=DoesNotExist;Trusted_Connection=True;" +
            "TrustServerCertificate=True;Connect Timeout=2";

        await using var factory = FactoryWithConnectionString(unreachableConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/systemstatus");
        response.EnsureSuccessStatusCode(); // must not be a 500 despite the DB being unreachable

        var body = await response.Content.ReadFromJsonAsync<SystemStatusResponse>();
        Assert.NotNull(body);
        Assert.False(body.database.reachable);
    }

    private sealed record SystemStatusResponse(bool localServerReachable, DatabaseStatus database, object backgroundRunner);
    private sealed record DatabaseStatus(bool reachable);
}
