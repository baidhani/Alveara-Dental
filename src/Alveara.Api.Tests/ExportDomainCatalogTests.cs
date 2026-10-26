using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01: the export catalog (<see cref="ExportDomainCatalog"/>) is compared with the database model, so "an export package says what it contains and what it leaves out" is a checked fact. The tests FAIL when a
/// table is added, renamed or removed, when a table that holds a patient id is left out of the package, or when something is excluded without a reason, until the catalog says what the export does about it. They need no
/// database: they read the model.
/// </summary>
public class ExportDomainCatalogTests
{
    private static readonly IModel Model = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer("Server=.;Database=ExportDomainCatalogModelOnly;Trusted_Connection=True").Options).Model;

    private static IReadOnlyList<string> ModelTables() => Model.GetEntityTypes().Select(e => e.GetTableName()).Where(t => t is not null).Select(t => t!).Distinct().Order().ToList();

    [Fact]
    public void Every_table_of_the_model_is_named_in_the_catalog_exactly_once()
    {
        var named = ExportDomainCatalog.AllTables.ToList();

        Assert.Equal(named.Count, named.Distinct().Count());                                                   // no table is in two places
        Assert.Empty(ModelTables().Except(named));                                                             // no table is forgotten
        Assert.Empty(named.Except(ModelTables()));                                                             // no name is stale
    }

    [Fact]
    public void Every_table_that_holds_a_patient_id_is_in_a_domain_the_package_carries()
    {
        var carried = ExportDomainCatalog.IncludedTables.ToHashSet();

        foreach (var table in PatientOwnedData.Tables.Concat(PatientOwnedData.Bookkeeping))
            Assert.True(carried.Contains(table), $"{table} holds a patient id but is not in a domain the package carries");
        foreach (var entity in Model.GetEntityTypes().Where(e => e.GetTableName() is not null))
            if (entity.GetProperties().Any(p => p.Name.EndsWith("PatientId", StringComparison.Ordinal)))
                Assert.True(carried.Contains(entity.GetTableName()!), $"{entity.GetTableName()} has a patient id column but is not in a domain the package carries");
    }

    [Fact]
    public void Every_exclusion_has_a_reason_and_every_domain_a_stable_key_and_a_title()
    {
        Assert.All(ExportDomainCatalog.Excluded, g =>
        {
            Assert.Matches("^[a-z_]+$", g.Key);
            Assert.True(g.Reason.Length >= 40, $"{g.Key}: the reason must say why in words");
            Assert.NotEmpty(g.Tables);
        });
        Assert.All(ExportDomainCatalog.Domains, d =>
        {
            Assert.Matches("^[a-z_]+$", d.Key);                                                                // becomes data/<key>.json
            Assert.False(string.IsNullOrWhiteSpace(d.Title));
            Assert.NotEmpty(d.Tables);
        });
        var keys = ExportDomainCatalog.Domains.Select(d => d.Key).Concat(ExportDomainCatalog.Excluded.Select(g => g.Key)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void A_table_that_holds_credentials_or_keys_is_never_in_a_domain_the_package_carries()
    {
        var secretColumns = new[] { "PasswordHash", "MfaSecret", "TokenHash", "CodeHash", "SecretKey", "Salt" };
        var carried = ExportDomainCatalog.IncludedTables.Concat(ExportDomainCatalog.LookupTables).ToHashSet();

        foreach (var entity in Model.GetEntityTypes().Where(e => e.GetTableName() is not null && carried.Contains(e.GetTableName()!)))
            foreach (var property in entity.GetProperties())
                Assert.False(secretColumns.Any(s => property.Name.Contains(s, StringComparison.OrdinalIgnoreCase)),
                    $"{entity.GetTableName()}.{property.Name} looks like a secret but its table is in the package");
    }

    [Fact]
    public void The_catalog_names_the_domains_and_exclusion_groups_the_manifest_will_report_today()
    {
        // not a limit: the first three tests are the guard. This pins what the package was designed against, so a change is noticed in review.
        Assert.Equal(19, ExportDomainCatalog.Domains.Count);
        Assert.Equal(5, ExportDomainCatalog.Excluded.Count);
    }
}
