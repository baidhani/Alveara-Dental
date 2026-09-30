using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>ALV-N009: confirming MFA enrollment rotates SecurityStamp by design (it invalidates
/// every OTHER outstanding session - see AccountService.ConfirmMfaEnrollmentAsync), but must not
/// silently invalidate the confirming caller's OWN current session cookie too. This was a real,
/// previously-undetected gap: nothing reacted to the stale cookie client-side until ALV-N009 added
/// router-level auth guards, which immediately surfaced it as "successfully enabled MFA, then
/// instantly redirected to a session-expired sign-in screen."</summary>
public class AuthControllerMfaConfirmSessionTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        });

    [Fact]
    public async Task Confirming_MFA_enrollment_keeps_the_confirming_caller_s_own_session_valid()
    {
        await using var factory = CreateFactory();
        // Default client: HandleCookies = true, so it automatically carries whatever Set-Cookie
        // the server issues - including a re-issued one from mfa/confirm, exactly like a real browser.
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest(username, "password", IdentityTestHelpers.TestBootstrapSecret));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));

        var csrfResponse = await client.GetAsync("/api/auth/csrf-token");
        var csrf = (await csrfResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString()!;

        var enrollRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollRequest.Headers.Add("X-CSRF-Token", csrf);
        var enrollResponse = await client.SendAsync(enrollRequest);
        Assert.Equal(HttpStatusCode.OK, enrollResponse.StatusCode);
        var enrollBody = await enrollResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var secret = enrollBody.GetProperty("base32Secret").GetString()!;
        var code = Totp.GenerateCodeForTests(Totp.FromBase32(secret));

        var confirmRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/confirm")
        {
            Content = JsonContent.Create(new { code })
        };
        confirmRequest.Headers.Add("X-CSRF-Token", csrf);
        var confirmResponse = await client.SendAsync(confirmRequest);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        // The critical assertion: the SAME client, with whatever cookie it now holds (rotated or
        // not), can still reach an ordinary authenticated endpoint - it was never signed out by
        // its own confirm call.
        var permissionsResponse = await client.GetAsync("/api/auth/permissions");
        Assert.Equal(HttpStatusCode.OK, permissionsResponse.StatusCode);
        var permissionsBody = await permissionsResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(username, permissionsBody.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Confirming_an_MFA_factor_replacement_also_keeps_the_confirming_caller_s_own_session_valid()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest(username, "password", IdentityTestHelpers.TestBootstrapSecret));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));

        async Task<string> EnrollAndConfirmAsync()
        {
            var freshCsrfResponse = await client.GetAsync("/api/auth/csrf-token");
            var freshCsrf = (await freshCsrfResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString()!;
            var enrollRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll")
            {
                Content = JsonContent.Create(new { currentPassword = "password" })
            };
            enrollRequest.Headers.Add("X-CSRF-Token", freshCsrf);
            var enrollResponse = await client.SendAsync(enrollRequest);
            var enrollBody = await enrollResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var secret = enrollBody.GetProperty("base32Secret").GetString()!;
            var code = Totp.GenerateCodeForTests(Totp.FromBase32(secret));

            var confirmCsrfResponse = await client.GetAsync("/api/auth/csrf-token");
            var confirmCsrf = (await confirmCsrfResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString()!;
            var confirmRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/confirm")
            {
                Content = JsonContent.Create(new { code })
            };
            confirmRequest.Headers.Add("X-CSRF-Token", confirmCsrf);
            var confirmResponse = await client.SendAsync(confirmRequest);
            Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
            return secret;
        }

        await EnrollAndConfirmAsync(); // first factor
        await EnrollAndConfirmAsync(); // replacement - this is the rotation-on-replace path

        var permissionsResponse = await client.GetAsync("/api/auth/permissions");
        Assert.Equal(HttpStatusCode.OK, permissionsResponse.StatusCode);
    }
}
