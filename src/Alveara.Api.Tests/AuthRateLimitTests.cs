using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-001-C01 "rate-limit/throttle repeated authentication attempts": distinct from
/// AccountService's per-account lockout, this throttles by caller IP across all auth-attempt
/// endpoints (login, register, bootstrap-admin, mfa/challenge, reset-password/complete), so a
/// caller spreading failures across many different usernames is still stopped.
/// </summary>
public class AuthRateLimitTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(int permitLimit) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
            builder.UseSetting("AuthAttemptRateLimit:PermitLimit", permitLimit.ToString());
            builder.UseSetting("AuthAttemptRateLimit:WindowSeconds", "60");
        });

    [Fact]
    public async Task Repeated_login_attempts_past_the_configured_limit_are_rejected_with_429()
    {
        await using var factory = CreateFactory(permitLimit: 3);
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest($"nobody-{i}", "irrelevant"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // under the limit: normal invalid-credentials response
        }

        var throttled = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody-4", "irrelevant"));

        Assert.Equal((HttpStatusCode)429, throttled.StatusCode);
    }

    [Fact]
    public async Task The_rate_limit_is_shared_across_different_usernames_not_reset_by_switching_accounts()
    {
        // Proves this is a caller-level throttle, not per-account lockout: hammering different
        // usernames from the same caller still trips the limit.
        await using var factory = CreateFactory(permitLimit: 3);
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest($"distinct-user-{Guid.NewGuid():N}", "irrelevant"));
        }

        var throttled = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest($"distinct-user-{Guid.NewGuid():N}", "irrelevant"));

        Assert.Equal((HttpStatusCode)429, throttled.StatusCode);
    }

    [Fact]
    public async Task Registration_attempts_past_the_limit_are_also_throttled()
    {
        await using var factory = CreateFactory(permitLimit: 3);
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest($"user-{Guid.NewGuid():N}", "password"));
        }

        var throttled = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest($"user-{Guid.NewGuid():N}", "password"));

        Assert.Equal((HttpStatusCode)429, throttled.StatusCode);
    }
}
