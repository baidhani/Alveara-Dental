using System.Net;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Alveara.Api.TestSupport;

/// <summary>Whether the test-support route may be mapped, and if not, why (the first failed condition).</summary>
public sealed record TestSupportDecision(bool Enabled, bool Requested, string? Refusal)
{
    /// <summary>The route was asked for (<c>TestSupport:Enabled=true</c>) but a condition failed: the host must refuse to start.</summary>
    public bool MustRefuseStart => Requested && !Enabled;
}

/// <summary>
/// ALV-N030 (brief 17.2, review R07): the single place that decides whether <c>POST /api/test-support/accounts</c> exists. All three must hold:
/// the environment name is exactly <c>Test</c>; <c>TestSupport:Enabled</c> is <c>true</c>; every configured listen address is loopback
/// (<c>127.0.0.1</c>, <c>::1</c>, <c>localhost</c>). The controller is <c>internal</c>, so MVC never discovers it on its own (not even in a process
/// that happens to have this assembly loaded); only <see cref="AddTestSupport"/> adds it, through an explicit feature provider.
/// </summary>
public static class TestSupportRegistration
{
    public const string EnvironmentName = "Test";
    public const string EnabledKey = "TestSupport:Enabled";

    /// <summary>The listen addresses a configuration names: <c>urls</c> (also set by <c>--urls</c> and <c>ASPNETCORE_URLS</c>) and <c>Kestrel:Endpoints:*:Url</c>.</summary>
    public static IEnumerable<string> ConfiguredListenAddresses(IConfiguration config)
    {
        foreach (var key in new[] { "urls", "URLS", "ASPNETCORE_URLS" })
            foreach (var url in (config[key] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return url;
        foreach (var endpoint in config.GetSection("Kestrel:Endpoints").GetChildren())
            if (endpoint["Url"] is { Length: > 0 } url) yield return url;
    }

    /// <summary>True only for an http(s) address whose host is 127.0.0.1, ::1 or localhost. Wildcards (<c>*</c>, <c>+</c>, <c>0.0.0.0</c>, <c>[::]</c>), LAN addresses and other names are false.</summary>
    public static bool IsLoopback(string address)
    {
        var text = address.Trim();
        // Kestrel's wildcard forms are not valid URIs ("http://*:5000", "http://+:5000"): never loopback.
        if (text.Contains("://*") || text.Contains("://+")) return false;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;
        var host = uri.Host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var ip) && (ip.Equals(IPAddress.Loopback) || ip.Equals(IPAddress.IPv6Loopback));
    }

    public static TestSupportDecision Evaluate(string environmentName, IConfiguration config, IEnumerable<string> extraListenAddresses)
    {
        var requested = string.Equals(config[EnabledKey], "true", StringComparison.OrdinalIgnoreCase);
        if (!requested) return new TestSupportDecision(false, false, "TestSupport:Enabled is not true");
        if (!string.Equals(environmentName, EnvironmentName, StringComparison.Ordinal))
            return new TestSupportDecision(false, true, $"the environment is '{environmentName}', not '{EnvironmentName}'");
        var bad = ConfiguredListenAddresses(config).Concat(extraListenAddresses).FirstOrDefault(a => !IsLoopback(a));
        if (bad is not null) return new TestSupportDecision(false, true, $"the listen address '{bad}' is not loopback");
        return new TestSupportDecision(true, true, null);
    }

    /// <summary>Adds the route's controller and service. Call only with an <see cref="TestSupportDecision.Enabled"/> decision.</summary>
    public static IServiceCollection AddTestSupport(this IServiceCollection services, TestSupportDecision decision)
    {
        if (!decision.Enabled) throw new InvalidOperationException($"Test support may not be enabled: {decision.Refusal}.");
        services.AddScoped<TestAccountService>();
        services.AddMvcCore().ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new TestSupportControllerProvider()));
        return services;
    }

    /// <summary>Adds exactly the one internal controller; nothing else in this assembly becomes a controller.</summary>
    private sealed class TestSupportControllerProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var type = typeof(TestAccountsController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type);
        }
    }
}
