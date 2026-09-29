using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

public class AuthControllerTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AuthControllerTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
        });

    [Fact]
    public async Task POST_register_with_a_new_username_returns_201_with_the_created_account()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(username, "a-strong-password", "Dentist"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.Equal(username, body!.Username);
        Assert.Equal("Dentist", body.Role);
    }

    [Fact]
    public async Task POST_register_with_an_already_taken_username_returns_409()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password-1", "Billing"));

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password-2", "Billing"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task POST_register_with_an_unrecognized_role_returns_400()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}", "password", "SpaceCaptain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_login_with_the_correct_password_returns_200()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "right-password", "Assistant"));

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "right-password"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task POST_login_with_an_unknown_username_and_a_wrong_password_produce_identical_responses_no_enumeration()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "right-password", "FrontDesk"));

        var wrongPasswordResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "wrong-password"));
        var unknownUserResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest($"nobody-{Guid.NewGuid():N}", "irrelevant"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUserResponse.StatusCode);
        var wrongPasswordBody = await wrongPasswordResponse.Content.ReadAsStringAsync();
        var unknownUserBody = await unknownUserResponse.Content.ReadAsStringAsync();
        Assert.Equal(wrongPasswordBody, unknownUserBody); // identical response either way — no username-enumeration signal
    }

    [Fact]
    public async Task POST_login_five_times_wrong_then_locks_the_account_and_returns_423()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "right-password", "OfficeManager"));

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "wrong-password"));
        }

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "right-password"));

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
    }
}
