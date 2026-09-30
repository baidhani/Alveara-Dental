using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Architecture.Identity;
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

    [Fact]
    public async Task Repeated_MFA_replacement_step_up_attempts_past_the_limit_are_also_throttled()
    {
        // R03 (review finding ALV-001-C01-R02-02): the step-up password verifier on mfa/enroll
        // must be covered by the same IP-based throttle as every other auth-attempt endpoint, not
        // just the account-level lockout AccountServiceMfaTests already proves separately.
        //
        // Confirming MFA rotates the account's SecurityStamp (by design - see the R01-02
        // correction), which invalidates the *current* session cookie immediately, so the client
        // must log in again (now via the MFA challenge, since MfaEnabled is true) before it can
        // make the step-up call this test is actually targeting.
        //
        // permitLimit=5 exactly covers the five setup calls below (bootstrap, first login, the
        // initial mfa/enroll, the second login, and the mfa/challenge that re-establishes a valid
        // session) - so the very next mfa/enroll call, regardless of outcome, is already over the
        // limit. This avoids relying on precise segment-boundary timing for a "several attempts
        // in, then throttled" count.
        await using var factory = CreateFactory(permitLimit: 5);
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest(username, "password", IdentityTestHelpers.TestBootstrapSecret)); // request 1
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password")); // request 2
        var csrfResponse = await client.GetAsync("/api/auth/csrf-token"); // not rate-limited
        var csrf = (await csrfResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString()!;

        // Establish an active MFA factor so a later mfa/enroll call takes the step-up
        // (current-password) path rather than the no-password-required initial-enrollment path.
        var enrollRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollRequest.Headers.Add("X-CSRF-Token", csrf);
        var enrollResponse = await client.SendAsync(enrollRequest); // request 3
        var enrolled = await enrollResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var secret = enrolled.GetProperty("base32Secret").GetString()!;
        var confirmRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/confirm")
        {
            Content = JsonContent.Create(new ConfirmMfaRequest(Totp.GenerateCodeForTests(Totp.FromBase32(secret)))),
        };
        confirmRequest.Headers.Add("X-CSRF-Token", csrf);
        await client.SendAsync(confirmRequest); // mfa/confirm isn't rate-limited (not a public/unauthenticated attempt endpoint)

        // Re-authenticate: the old cookie is now stale (SecurityStamp rotated by confirm above).
        var reLoginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password")); // request 4
        var reLoginBody = await reLoginResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var challengeToken = reLoginBody.GetProperty("challengeToken").GetString()!;
        await client.PostAsJsonAsync("/api/auth/mfa/challenge",
            new MfaChallengeRequest(challengeToken, Totp.GenerateCodeForTests(Totp.FromBase32(secret)))); // request 5 - now at the limit, and this issues a fresh valid session cookie

        var stepUpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll")
        {
            Content = JsonContent.Create(new EnrollMfaRequest("wrong-password")),
        };
        stepUpRequest.Headers.Add("X-CSRF-Token", csrf);
        var throttled = await client.SendAsync(stepUpRequest); // request 6 - over the limit

        Assert.Equal((HttpStatusCode)429, throttled.StatusCode);
    }
}
