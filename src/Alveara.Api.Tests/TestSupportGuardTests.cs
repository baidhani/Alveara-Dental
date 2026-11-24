using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.ChainHost;
using Alveara.Api.Data;
using Alveara.Api.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 slice 5a (brief section 17, review BRIEFING-R07): guards G1 to G7 for the test-only account route <c>POST /api/test-support/accounts</c>.
/// <list type="bullet">
/// <item>The product never maps it, in any environment, with any setting (G1, G6); it references no test project (G2) and would publish no test assembly (G3).</item>
/// <item>ChainHost maps it only with all three conditions (Test, opted in, loopback); a requested route that may not be enabled refuses the start (G4, G7).</item>
/// <item>The setting and the host's name appear in no dev, deployment or product configuration file (G5).</item>
/// </list>
/// G3 reads MSBuild's publish file list (<c>ComputeResolvedFilesToPublishList</c>) instead of running a full publish: a full publish of Alveara.Api also runs
/// <c>npm ci</c> and <c>npm run build</c> (Alveara.Api.csproj), which would reinstall the client's packages; the list is exactly what a publish copies.
/// </summary>
public class TestSupportGuardTests : IAsyncLifetime
{
    private const string Route = "/api/test-support/accounts";
    private const string StaffRoute = "/api/test-support/staff-profiles";   // ALV-N030 R07 implementation extension (owner, 2026-11-24)

    private async Task<int> ProfilesNamedAsync(string displayName)
    {
        await using var db = _fixture.CreateContext();
        return await db.StaffProfiles.CountAsync(s => s.DisplayName == displayName);
    }
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Src = Path.Combine(RepoRoot, "src");

    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static object Body(string username, string role = "Admin") => new CreateTestAccountRequest(username, "Fixture-Password-1", role);

    private async Task<int> AccountsNamedAsync(string username)
    {
        await using var db = _fixture.CreateContext();
        return await db.UserAccounts.CountAsync(u => u.Username == username);
    }

    // ---------- G1: the product host never maps the route ----------

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Test")]
    public async Task G1_the_product_never_maps_the_route_whatever_the_environment_or_settings(string environment)
    {
        await using var product = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            b.UseSetting(TestSupportRegistration.EnabledKey, "true");
            b.UseSetting("test-support", "true");          // what "--test-support" on the command line becomes
            b.UseSetting("urls", "http://127.0.0.1:5999");
        });
        var client = product.CreateClient();
        var response = await client.PostAsJsonAsync(Route, Body($"g1-{environment}"));
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(0, await AccountsNamedAsync($"g1-{environment}"));
        var staff = await client.PostAsJsonAsync(StaffRoute, new CreateTestStaffProfileRequest($"G1 Profile {environment}", LegacyUnlinked: true));
        Assert.Contains(staff.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(0, await ProfilesNamedAsync($"G1 Profile {environment}"));
    }

    // ---------- G2: the reference graph ----------

    [Fact]
    public void G2_only_the_chain_host_and_the_tests_reference_test_support_and_the_product_references_no_test_project()
    {
        var projects = Directory.GetFiles(Src, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"))
            .ToDictionary(p => Path.GetFileNameWithoutExtension(p), File.ReadAllText);
        var product = projects["Alveara.Api"];
        foreach (var forbidden in new[] { "Alveara.Api.TestSupport", "Alveara.Api.ChainHost", "Alveara.Api.Tests" })
            Assert.DoesNotContain(forbidden, product);
        var referencing = projects.Where(p => p.Value.Contains(@"Alveara.Api.TestSupport\Alveara.Api.TestSupport.csproj") || p.Value.Contains("Alveara.Api.TestSupport/Alveara.Api.TestSupport.csproj"))
            .Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Alveara.Api.ChainHost", "Alveara.Api.Tests" }, referencing);

        var productSource = Directory.GetFiles(Path.Combine(Src, "Alveara.Api"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        Assert.DoesNotContain(productSource, p => File.ReadAllText(p).Contains("TestSupport"));
    }

    // ---------- G3: what a publish of the product would ship ----------

    [Fact]
    public async Task G3_a_publish_of_the_product_ships_no_test_assembly()
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Src,
        };
        foreach (var a in new[] { "msbuild", Path.Combine("Alveara.Api", "Alveara.Api.csproj"), "-t:ComputeResolvedFilesToPublishList", "-getItem:ResolvedFileToPublish", "-nologo" })
            psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await error + await output);

        using var json = JsonDocument.Parse(await output);
        var shipped = json.RootElement.GetProperty("Items").GetProperty("ResolvedFileToPublish").EnumerateArray()
            .Select(i => i.GetProperty("RelativePath").GetString()!).ToList();
        Assert.Contains("Alveara.Api.dll", shipped);   // the list is real, not empty
        Assert.DoesNotContain(shipped, f => f.Contains("Alveara.Api.TestSupport", StringComparison.OrdinalIgnoreCase) || f.Contains("Alveara.Api.ChainHost", StringComparison.OrdinalIgnoreCase)
            || f.Contains("Alveara.Api.Tests", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- G4: ChainHost maps the route only with all three conditions ----------

    public static IEnumerable<object[]> Combinations() =>
        from env in new[] { "Test", "Development" }
        from enabled in new[] { true, false }
        from loopback in new[] { true, false }
        select new object[] { env, enabled, loopback };

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task G4_the_chain_host_has_the_route_only_with_test_opt_in_and_loopback(string environment, bool enabled, bool loopback)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TestSupportRegistration.EnabledKey] = enabled ? "true" : null }).Build();
        var address = loopback ? "http://127.0.0.1:5072" : "http://0.0.0.0:5072";
        var decision = TestSupportRegistration.Evaluate(environment, config, [address]);
        var all = environment == "Test" && enabled && loopback;
        Assert.Equal(all, decision.Enabled);

        // A process start: refused when the route was asked for but may not be had, and always for a non-loopback address.
        var refusal = ChainHostMain.StartRefusal(address, decision);
        Assert.Equal(!loopback || (enabled && !all), refusal is not null);
        if (refusal is not null) return;   // nothing would start

        var username = $"g4-{environment}-{enabled}-{loopback}".ToLowerInvariant();
        await using var host = new ChainHostFactory(null, null, new ChainHostSettings(environment, decision))
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString));
        var client = host.CreateClient();
        var response = await client.PostAsJsonAsync(Route, Body(username));
        var profileName = $"G4 Profile {environment} {enabled} {loopback}";
        var staff = await client.PostAsJsonAsync(StaffRoute, new CreateTestStaffProfileRequest(profileName, LegacyUnlinked: true));
        if (!all)
        {
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
            Assert.Equal(0, await AccountsNamedAsync(username));
            Assert.Contains(staff.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
            Assert.Equal(0, await ProfilesNamedAsync(profileName));
            return;
        }
        Assert.Equal(HttpStatusCode.Created, staff.StatusCode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = _fixture.CreateContext();
        var account = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Username == username);
        Assert.Equal((Role.Admin, AccountKind.Staff, false, false), (account.Role, account.AccountKind, account.IsDisabled, account.MustChangePassword));
    }

    [Fact]
    public async Task G4_the_route_creates_exactly_what_is_asked_and_signs_in_over_http_under_test()
    {
        var host = TestHost();
        await using var _ = host;
        var client = host.CreateClient();
        var created = await client.PostAsJsonAsync(Route, new CreateTestAccountRequest("g4.staff", "Fixture-Password-1", "FrontDesk", WithStaffProfile: true, MustChangePassword: true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var staffId = body.GetProperty("staffProfileId").GetGuid();
        await using (var db = _fixture.CreateContext())
        {
            var account = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Username == "g4.staff");
            Assert.True(account.MustChangePassword);
            Assert.Equal(account.Id, await db.StaffProfiles.Where(s => s.Id == staffId).Select(s => s.UserAccountId).SingleAsync());
        }
        var deploy = await client.PostAsJsonAsync(Route, new CreateTestAccountRequest("g4.deploy", "Fixture-Password-1", "Admin", Kind: "DeploymentAdministrator"));
        Assert.Equal("DeploymentAdministrator", (await deploy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountKind").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route, new CreateTestAccountRequest("g4.bad", "x", "FrontDesk", Kind: "DeploymentAdministrator"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Route, new CreateTestAccountRequest("g4.staff", "x", "FrontDesk"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route, new CreateTestAccountRequest("", "x", "FrontDesk"))).StatusCode);

        // Under Test the product would mark the cookie Secure-only; the chain host relaxes that for plain HTTP on loopback, so a sign-in holds.
        await client.PostAsJsonAsync(Route, Body("g4.signin"));
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "g4.signin", password = "Fixture-Password-1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.DoesNotContain(login.Headers.GetValues("Set-Cookie"), c => c.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/permissions")).StatusCode);
    }

    [Fact]
    public async Task G4_the_staff_profile_fixture_links_only_an_existing_unlinked_account_and_makes_an_unlinked_profile_only_when_asked()
    {
        var host = TestHost();
        await using var _ = host;
        var client = host.CreateClient();
        var account = (await (await client.PostAsJsonAsync(Route, Body("g4.link", "Dentist"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var linked = await client.PostAsJsonAsync(StaffRoute, new CreateTestStaffProfileRequest("Dr. Linked Fixture", "Dentist", account));
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);
        var body = await linked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(account, body.GetProperty("userAccountId").GetGuid());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("rowVersion").GetString()));
        await using (var db = _fixture.CreateContext())
            Assert.Equal(account, await db.StaffProfiles.Where(s => s.DisplayName == "Dr. Linked Fixture").Select(s => s.UserAccountId).SingleAsync());

        async Task<(HttpStatusCode, string)> Refused(CreateTestStaffProfileRequest r)
        {
            var response = await client.PostAsJsonAsync(StaffRoute, r);
            return (response.StatusCode, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()!);
        }
        Assert.Equal((HttpStatusCode.Conflict, "account_already_linked"), await Refused(new("Second Profile", null, account)));
        Assert.Equal((HttpStatusCode.NotFound, "account_not_found"), await Refused(new("Ghost Profile", null, Guid.NewGuid())));
        Assert.Equal((HttpStatusCode.BadRequest, "legacy_unlinked_required"), await Refused(new("Unflagged Profile")));
        Assert.Equal((HttpStatusCode.BadRequest, "display_name_required"), await Refused(new("  ", null, null, true)));
        Assert.Equal((HttpStatusCode.Conflict, "staff_name_taken"), await Refused(new("Dr. Linked Fixture", null, null, true)));
        foreach (var name in new[] { "Second Profile", "Ghost Profile", "Unflagged Profile" }) Assert.Equal(0, await ProfilesNamedAsync(name));

        var legacy = await client.PostAsJsonAsync(StaffRoute, new CreateTestStaffProfileRequest("Legacy Fixture", null, null, LegacyUnlinked: true));
        Assert.Equal(HttpStatusCode.Created, legacy.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await legacy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userAccountId").ValueKind);
    }

    /// <summary>ChainHost as the runners start it (Test, opted in, loopback; real clock), in memory against the test database.</summary>
    private WebApplicationFactory<Program> TestHost()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TestSupportRegistration.EnabledKey] = "true" }).Build();
        var decision = TestSupportRegistration.Evaluate("Test", config, ["http://127.0.0.1:5072"]);
        return new ChainHostFactory(null, null, new ChainHostSettings("Test", decision))
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString));
    }

    // ---------- G5: where the setting and the host may be named ----------

    [Fact]
    public void G5_no_dev_deployment_or_product_configuration_file_names_the_test_support_setting_or_the_chain_host()
    {
        var forbidden = new List<string>
        {
            Path.Combine(RepoRoot, "scripts", "Start-DevApi.ps1"),
            Path.Combine(RepoRoot, "scripts", "New-DevAdmin.ps1"),
        };
        forbidden.AddRange(Directory.GetFiles(Path.Combine(Src, "Alveara.Api"), "appsettings*.json"));
        forbidden.AddRange(Directory.GetFiles(Path.Combine(Src, "Alveara.Api"), "*.*", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && (p.EndsWith(".cs") || p.EndsWith(".csproj") || p.EndsWith(".json") || p.EndsWith(".pubxml"))));
        foreach (var pattern in new[] { "Dockerfile*", "docker-compose*", "*.pubxml" })
            forbidden.AddRange(Directory.GetFiles(RepoRoot, pattern, SearchOption.AllDirectories).Where(p => !p.Contains("node_modules") && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")));
        foreach (var dir in new[] { Path.Combine(RepoRoot, "build"), Path.Combine(RepoRoot, "ops") })
            if (Directory.Exists(dir)) forbidden.AddRange(Directory.GetFiles(dir, "*.ps1", SearchOption.AllDirectories));
        forbidden.AddRange(Directory.GetFiles(Path.Combine(RepoRoot, "scripts"), "*.ps1").Where(p => Path.GetFileName(p) is var n && (n.Contains("Install", StringComparison.OrdinalIgnoreCase) || n.Contains("Deploy", StringComparison.OrdinalIgnoreCase) || n.Contains("Publish", StringComparison.OrdinalIgnoreCase) || n.Contains("Setup", StringComparison.OrdinalIgnoreCase))));

        Assert.NotEmpty(forbidden);
        var offenders = forbidden.Distinct().Where(File.Exists)
            .Where(p => File.ReadAllText(p) is var text && (text.Contains("TestSupport__Enabled") || text.Contains("TestSupport:Enabled") || text.Contains("Alveara.Api.ChainHost") || text.Contains("Alveara.Api.TestSupport")))
            .Select(p => Path.GetRelativePath(RepoRoot, p)).ToList();
        Assert.Empty(offenders);
    }

    // ---------- G6: the product's OpenAPI document ----------

    [Fact]
    public async Task G6_the_product_openapi_document_does_not_list_the_route()
    {
        await using var product = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            b.UseSetting(TestSupportRegistration.EnabledKey, "true");
        });
        var response = await product.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/setup/status", document);   // the document is the real one
        Assert.DoesNotContain("test-support", document);
    }

    // ---------- G7: loopback only ----------

    [Theory]
    [InlineData("http://127.0.0.1:5072", true)]
    [InlineData("http://[::1]:5072", true)]
    [InlineData("http://localhost:5072", true)]
    [InlineData("https://LOCALHOST:5073", true)]
    [InlineData("http://0.0.0.0:5072", false)]
    [InlineData("http://*:5072", false)]
    [InlineData("http://+:5072", false)]
    [InlineData("http://[::]:5072", false)]
    [InlineData("http://192.168.1.20:5072", false)]
    [InlineData("http://10.0.0.5:5072", false)]
    [InlineData("http://dental-server:5072", false)]
    [InlineData("http://127.0.0.1.example.com:5072", false)]
    [InlineData("ftp://127.0.0.1:5072", false)]
    [InlineData("not a url", false)]
    public void G7_only_loopback_addresses_pass(string address, bool expected) =>
        Assert.Equal(expected, TestSupportRegistration.IsLoopback(address));

    [Fact]
    public void G7_every_configured_address_is_checked_and_the_default_chain_url_is_loopback()
    {
        Assert.True(TestSupportRegistration.IsLoopback(ChainHostMain.DefaultUrl));
        Assert.StartsWith("http://127.0.0.1:", ChainHostMain.DefaultUrl);
        foreach (var (key, value) in new[]
        {
            ("urls", "http://127.0.0.1:5072;http://0.0.0.0:5080"),
            ("ASPNETCORE_URLS", "http://+:5080"),
            ("Kestrel:Endpoints:Lan:Url", "http://192.168.1.20:5080"),
        })
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TestSupportRegistration.EnabledKey] = "true", [key] = value }).Build();
            var decision = TestSupportRegistration.Evaluate("Test", config, [ChainHostMain.DefaultUrl]);
            Assert.False(decision.Enabled);
            Assert.True(decision.MustRefuseStart, key);
        }
    }

    [Theory]
    [InlineData("http://0.0.0.0:5099", null, "real")]                       // a wildcard CHAIN_URL: always refused
    [InlineData("http://127.0.0.1:5099", "http://0.0.0.0:5098", "real")]     // test support requested with a wildcard ASPNETCORE_URLS
    [InlineData("http://127.0.0.1:5099", null, null)]                        // no clock mode stated
    [InlineData("http://127.0.0.1:5099", null, "real", "--urls", "http://0.0.0.0:5097")]   // test support requested with a wildcard --urls on the command line
    public async Task G7_a_real_chain_host_process_refuses_to_start(string chainUrl, string? aspnetcoreUrls, string? clockMode, params string[] args)
    {
        var dll = Path.Combine(Src, "Alveara.Api.ChainHost", "bin", "Debug", "net10.0", "Alveara.Api.ChainHost.dll");
        Assert.True(File.Exists(dll), $"build the chain host first: {dll}");
        var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.ArgumentList.Add(dll);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["CHAIN_URL"] = chainUrl;
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Test";
        psi.Environment["TestSupport__Enabled"] = "true";
        psi.Environment["ConnectionStrings__Alveara"] = _fixture.ConnectionString;
        if (aspnetcoreUrls is null) psi.Environment.Remove("ASPNETCORE_URLS"); else psi.Environment["ASPNETCORE_URLS"] = aspnetcoreUrls;
        if (clockMode is null) psi.Environment.Remove("CHAIN_CLOCK_MODE"); else psi.Environment["CHAIN_CLOCK_MODE"] = clockMode;

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); Assert.Fail("the chain host started instead of refusing"); }
        Assert.NotEqual(0, process.ExitCode);
        Assert.DoesNotContain("ChainHost ready", await stdout);
        Assert.False(string.IsNullOrWhiteSpace(await stderr));
    }

    // ---------- G8: the runners start the chain host as R07 requires ----------

    [Theory]
    [InlineData("Invoke-RealBackendWalkthrough.ps1")]
    [InlineData("Invoke-AccumulatedChain.ps1")]
    public void G8_each_runner_starts_the_chain_host_under_test_with_test_support_an_explicit_clock_mode_and_a_loopback_address(string runner)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "scripts", runner));
        Assert.Matches(@"\$chainUrl\s*=\s*'http://127\.0\.0\.1:5072'", text);
        Assert.Contains("$psi.EnvironmentVariables['CHAIN_URL'] = $chainUrl", text);
        Assert.Contains("$psi.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Test'", text);
        Assert.Contains("$psi.EnvironmentVariables['TestSupport__Enabled'] = 'true'", text);
        Assert.Contains("$psi.EnvironmentVariables['CHAIN_CLOCK_MODE']", text);
        Assert.Contains("$psi.EnvironmentVariables.Remove('ASPNETCORE_URLS')", text);
        Assert.Contains(@"Alveara.Api.ChainHost\bin\Debug\net10.0\Alveara.Api.ChainHost.dll", text);
        foreach (var gone in new[] { "Alveara.Api.dll", "AdminBootstrapSecret", "E2E_BOOTSTRAP_SECRET", "'Development'" })
            Assert.DoesNotContain(gone, text);
    }

    [Fact]
    public void G8_the_vite_proxy_targets_the_loopback_address_the_runners_use()
    {
        var vite = File.ReadAllText(Path.Combine(Src, "alveara-client", "vite.config.ts"));
        Assert.Contains("const API_DEV_PORT = 5072;", vite);
        Assert.Contains("target: `http://127.0.0.1:${API_DEV_PORT}`", vite);
    }
}
