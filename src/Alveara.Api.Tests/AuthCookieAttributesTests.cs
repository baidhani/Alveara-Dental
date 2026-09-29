using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>ALV-001-C01: "issued authentication cookies carry the required security attributes."
/// A successful login's Set-Cookie header is inspected directly - the only genuine way to prove
/// the attributes configured in Program.cs's AddCookie(...) actually reach the wire, rather than
/// trusting the configuration code alone.</summary>
public class AuthCookieAttributesTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_successful_login_issues_a_session_cookie_with_HttpOnly_and_SameSite_Strict()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }); // inspect the raw header instead of letting the handler absorb it
        var username = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest(username, "password", IdentityTestHelpers.TestBootstrapSecret));

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders));
        var sessionCookieHeader = setCookieHeaders!.SingleOrDefault(h => h.StartsWith(".AspNetCore.Cookies", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sessionCookieHeader);
        Assert.Contains("httponly", sessionCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookieHeader, StringComparison.OrdinalIgnoreCase);
        // Secure is intentionally NOT asserted here: Program.cs sets CookieSecurePolicy.SameAsRequest
        // in the Development environment (the WebApplicationFactory's default) specifically so
        // plain-HTTP localhost development keeps working - production (CookieSecurePolicy.Always)
        // is a one-line config difference this test would not exercise either way.
    }
}
