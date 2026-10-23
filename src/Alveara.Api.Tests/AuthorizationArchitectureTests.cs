using System.Text.RegularExpressions;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017 architecture guards, over the source of the API itself: every permission decision goes through the one effective-permission seam, so nothing outside the default matrix and the code that builds
/// the effective policy may read the static default table. A guard that cannot find the files it protects fails (a rename must not make it pass by looking at nothing).
/// </summary>
public class AuthorizationArchitectureTests
{
    private static readonly string ApiRoot = FindApiRoot();

    private static string FindApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Alveara.Api", "Alveara.Api.csproj"))) dir = dir.Parent;
        return dir is null ? throw new InvalidOperationException("The repository root was not found from the test output folder.") : Path.Combine(dir.FullName, "src", "Alveara.Api");
    }

    private static IEnumerable<(string Path, string Text)> Sources() =>
        Directory.EnumerateFiles(ApiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(ApiRoot, f).Replace('\\', '/'), File.ReadAllText(f)));

    /// <summary>The only files allowed to read the static default matrix: the matrix itself and the pure builder that turns defaults plus stored rows into the effective policy.</summary>
    private static readonly string[] MatrixReaders = ["Architecture/Identity/PermissionMatrix.cs", "Architecture/Permissions/PolicySnapshotBuilder.cs"];

    [Fact]
    public void Nothing_outside_the_default_matrix_and_the_policy_builder_reads_the_static_matrix()
    {
        var offenders = Sources().Where(s => Regex.IsMatch(s.Text, @"\bPermissionMatrix\s*\.") && !MatrixReaders.Contains(s.Path)).Select(s => s.Path).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_guard_can_see_the_files_it_protects()
    {
        var all = Sources().ToList();
        Assert.True(all.Count > 100, $"only {all.Count} source files found under {ApiRoot}");
        foreach (var reader in MatrixReaders) Assert.Contains(all, s => s.Path == reader);
        Assert.Contains(all, s => s.Path == "Architecture/Permissions/PolicySnapshotBuilder.cs" && Regex.IsMatch(s.Text, @"\bPermissionMatrix\s*\."));      // the builder really is a reader, so the allow-list is not vacuous
    }

    [Fact]
    public void The_permission_attributes_decide_through_the_seam_and_every_controller_permission_attribute_is_one_of_them()
    {
        var attributes = Sources().Where(s => s.Path is "Architecture/Identity/RequirePermissionAttribute.cs" or "Architecture/Identity/RequireAnyPermissionAttribute.cs").ToList();
        Assert.Equal(2, attributes.Count);
        Assert.All(attributes, a => Assert.Contains("IEffectivePermissions", a.Text));
        // any other filter that reads the caller's role claim to refuse a request is a second enforcement point the policy would not see
        var others = Sources().Where(s => s.Text.Contains("IAsyncActionFilter") && s.Text.Contains("ClaimTypes.Role") && s.Path is not ("Architecture/Identity/RequirePermissionAttribute.cs" or "Architecture/Identity/RequireAnyPermissionAttribute.cs")).Select(s => s.Path).ToList();
        Assert.Empty(others);
    }

    [Fact]
    public void The_enforcement_sites_are_still_the_attribute_and_the_inventory_has_not_shrunk()
    {
        var sources = Sources().ToList();
        var sites = sources.Sum(s => Regex.Matches(s.Text, @"\[RequirePermission\(").Count);
        var any = sources.Sum(s => Regex.Matches(s.Text, @"\[RequireAnyPermission\(").Count);
        Assert.True(sites >= 270, $"{sites} RequirePermission sites");                                                // the design counted 274; the number may grow with new routes, never silently shrink
        Assert.True(any >= 1);
    }

    /// <summary>The only file allowed to change an account role or enabled state, or to delete an account: the guarded account service. Registration and the first-administrator bootstrap create accounts in object initializers, which is not a write to an existing account.</summary>
    private const string AccountWriter = "Architecture/Identity/AccountService.cs";

    [Fact]
    public void An_accounts_role_and_disabled_state_are_written_and_accounts_deleted_only_by_the_guarded_account_service()
    {
        var offenders = Sources()
            .Where(s => s.Path != AccountWriter)
            .Where(s => Regex.IsMatch(s.Text, @"\.(Role|IsDisabled)\s*=[^=>]")                                                   // account.Role = ..., account.IsDisabled = ...
                     || Regex.IsMatch(s.Text, @"SetProperty\(\s*\w+\s*=>\s*\w+\.(Role|IsDisabled)\b")                           // ExecuteUpdate(s => s.SetProperty(u => u.Role, ...))
                     || Regex.IsMatch(s.Text, @"UserAccounts\s*\.\s*(Remove|RemoveRange)\b")
                     || Regex.IsMatch(s.Text, @"UserAccounts[^;]*ExecuteDelete"))
            .Select(s => s.Path).ToList();
        Assert.Empty(offenders);
        var service = Sources().Single(s => s.Path == AccountWriter).Text;
        Assert.Contains("account.Role = newRole;", service);                                                                // the guard really protects the writer it names
        Assert.Contains("account.IsDisabled = !enabled;", service);
        Assert.Contains("AdminAvailabilityGuard.AcquireAsync", service);
    }
}
