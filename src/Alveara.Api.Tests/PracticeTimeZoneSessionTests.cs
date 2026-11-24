using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A3 (briefing 4.1-1, 4.1-2, BR-5): the practice time zone the API publishes. Every successful authenticated <c>GET /api/auth/permissions</c>
/// carries <c>practiceTimeZoneId</c>, the canonical IANA id, for every role; a Windows-id configuration is published in its IANA form; a zone without an IANA
/// form is refused; and the server-owned Windows mapping (for a future Windows client) round-trips.
/// </summary>
public class PracticeTimeZoneSessionTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(string? practiceTimeZone = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AuthAttemptRateLimit:PermitLimit", "100");   // eight sign-ins in one test
            if (practiceTimeZone is not null) builder.UseSetting("PracticeTimeZone", practiceTimeZone);
        });

    private static HttpRequestMessage WithCsrf(HttpMethod method, string url, string csrf, object body)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf);
        return request;
    }

    private static async Task<(HttpClient Client, string Csrf)> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        await TestAccounts.SeedFirstAdminResponseAsync(factory, username, "admin-password");
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "admin-password"));
        var csrf = (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return (client, csrf);
    }

    private static async Task<HttpClient> UserAsync(WebApplicationFactory<Program> factory, HttpClient admin, string csrf, string role)
    {
        var username = $"user-{Guid.NewGuid():N}";
        var created = await (await TestAccounts.SeedRegisteredResponseAsync(factory, username, "password")).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        await admin.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{id}/role", csrf, new ChangeRoleRequest(role)));
        await admin.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{id}/enabled", csrf, new SetEnabledRequest(true)));
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        Assert.True(login.IsSuccessStatusCode, $"{role} sign-in: {(int)login.StatusCode}");
        return client;
    }

    private static async Task<JsonElement> PermissionsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Every_signed_in_role_gets_the_canonical_iana_practice_zone_and_a_signed_out_caller_gets_401()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        Assert.Equal("America/Chicago", (await PermissionsAsync(admin)).GetProperty("practiceTimeZoneId").GetString());
        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager", "Unassigned" })
        {
            var body = await PermissionsAsync(await UserAsync(factory, admin, csrf, role));
            Assert.Equal(role, body.GetProperty("role").GetString());
            Assert.Equal("America/Chicago", body.GetProperty("practiceTimeZoneId").GetString());   // no permission is needed, Unassigned included
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/auth/permissions")).StatusCode);
    }

    [Fact]
    public async Task A_windows_zone_configuration_is_published_as_its_iana_id()
    {
        await using var factory = CreateFactory("Central Standard Time");
        var (admin, _) = await AdminAsync(factory);
        Assert.Equal("America/Chicago", (await PermissionsAsync(admin)).GetProperty("practiceTimeZoneId").GetString());
    }

    [Theory]
    [InlineData("America/Chicago")]
    [InlineData("Asia/Tokyo")]
    [InlineData("America/Havana")]   // its clocks change at midnight
    public void An_iana_zone_is_published_as_is_and_the_server_owned_windows_mapping_round_trips(string iana)
    {
        Assert.Equal(iana, PracticeZoneIds.CanonicalIanaId(TimeZoneInfo.FindSystemTimeZoneById(iana)));
        Assert.True(PracticeZoneIds.TryWindowsId(iana, out var windows));
        Assert.False(string.IsNullOrEmpty(windows));
        Assert.Equal(iana, PracticeZoneIds.CanonicalIanaId(TimeZoneInfo.FindSystemTimeZoneById(windows!)));
    }

    [Fact]
    public void A_zone_with_no_iana_form_is_refused_and_never_published()
    {
        var custom = TimeZoneInfo.CreateCustomTimeZone("Alveara Test Zone", TimeSpan.FromHours(-6), "Alveara Test Zone", "Alveara Test Zone");
        var e = Assert.Throws<PracticeZoneConfigurationException>(() => PracticeZoneIds.CanonicalIanaId(custom));
        Assert.Equal("Alveara Test Zone", e.ConfiguredId);
        Assert.False(PracticeZoneIds.TryWindowsId("Not/A_Zone", out _));
    }
}
