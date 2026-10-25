using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-017: the registry of everything that holds a patient id (<see cref="PatientOwnedData"/>) is compared with the database model, so "a merge preserves linked data" is a checked fact and not a hope. The test FAILS when
/// someone adds, renames or removes a table or column that holds a patient id, makes one point somewhere other than the patient table, or adds a uniqueness rule that includes a patient id, until the registry says what a
/// merge does about it. It needs no database: it reads the model.
/// </summary>
public class PatientOwnedDataTests
{
    private static readonly IModel Model = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer("Server=.;Database=PatientOwnedDataModelOnly;Trusted_Connection=True").Options).Model;

    private static bool HoldsPatientId(IReadOnlyProperty p) => (p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?)) && p.Name.EndsWith("PatientId", StringComparison.Ordinal);

    private static IEnumerable<(IEntityType Entity, IProperty Property)> ModelColumns() =>
        Model.GetEntityTypes().Where(e => e.GetTableName() is not null).SelectMany(e => e.GetProperties().Where(p => HoldsPatientId(p)).Select(p => (e, p)));

    [Fact]
    public void The_registry_lists_exactly_the_columns_of_the_model_that_hold_a_patient_id()
    {
        var model = ModelColumns().Select(c => $"{c.Entity.GetTableName()}.{c.Property.Name}").Order().ToList();
        var registry = PatientOwnedData.All.Select(c => $"{c.Table}.{c.Column}").Order().ToList();
        Assert.Equal(model, registry);
        Assert.Equal(registry.Count, registry.Distinct().Count());
    }

    [Fact]
    public void A_column_is_marked_as_a_foreign_key_to_the_patient_table_exactly_when_the_model_says_it_is()
    {
        var model = ModelColumns().ToDictionary(c => $"{c.Entity.GetTableName()}.{c.Property.Name}",
            c => c.Entity.GetForeignKeys().Any(f => f.Properties.Count == 1 && f.Properties[0] == c.Property && f.PrincipalEntityType.ClrType == typeof(Patient)));
        foreach (var column in PatientOwnedData.All) Assert.True(model[$"{column.Table}.{column.Column}"] == column.ForeignKey, $"{column.Table}.{column.Column}: the registry says foreign key = {column.ForeignKey}, the model says {model[$"{column.Table}.{column.Column}"]}");
        Assert.Equal(21, PatientOwnedData.All.Count(c => !c.ForeignKey));            // the child, version and event tables that copy their parent's patient id
    }

    [Fact]
    public void The_columns_that_only_point_at_a_patient_are_exactly_the_five_references_and_everything_else_is_owned_data()
    {
        Assert.Equal(new[] { "BillingEntries.PaidByPatientId", "BillingResponsibilities.ResponsiblePatientId", "Messages.RelatedPatientId", "Notifications.RelatedPatientId", "Patients.GuarantorPatientId" },
            PatientOwnedData.References.Select(c => $"{c.Table}.{c.Column}").Order().ToArray());
        Assert.All(PatientOwnedData.Owned, c => Assert.Equal("PatientId", c.Column));
        Assert.DoesNotContain(PatientOwnedData.Owned, c => c.Table == "Patients");
    }

    [Fact]
    public void Every_uniqueness_rule_that_includes_a_patient_id_is_declared_with_how_a_merge_handles_a_collision()
    {
        var model = new List<string>();
        foreach (var entity in Model.GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            foreach (var index in entity.GetIndexes().Where(i => i.IsUnique && i.Properties.Any(HoldsPatientId)))
                model.Add($"{entity.GetTableName()}:{string.Join(",", index.Properties.Select(p => p.Name))}");
            var key = entity.FindPrimaryKey();
            if (key is not null && key.Properties.Any(HoldsPatientId)) model.Add($"{entity.GetTableName()}:{string.Join(",", key.Properties.Select(p => p.Name))}");
        }
        var registry = PatientOwnedData.All.SelectMany(c => c.UniqueRules.Select(u => $"{c.Table}:{u.Columns}")).ToList();
        Assert.Equal(model.Order().ToList(), registry.Order().ToList());
    }

    [Fact]
    public void A_rule_on_the_patient_id_alone_is_one_per_patient_a_replay_key_rule_is_a_replay_key_and_the_rest_are_natural_keys()
    {
        foreach (var rule in PatientOwnedData.All.SelectMany(c => c.UniqueRules))
        {
            var expected = rule.Columns == "PatientId" ? MergeCollisionPolicy.OnePerPatient
                : rule.Columns.Contains("IdempotencyKey") || rule.Columns.Contains("CreateKey") ? MergeCollisionPolicy.ReplayKey : MergeCollisionPolicy.NaturalKey;
            Assert.Equal(expected, rule.Policy);
        }
        Assert.Equal(new[] { "BillingAccounts", "PatientRecallPlans", "PerioSessions" }, PatientOwnedData.All.Where(c => c.UniqueRules.Any(u => u.Policy == MergeCollisionPolicy.OnePerPatient)).Select(c => c.Table).Order().ToArray());
    }

    [Fact]
    public void The_registry_names_fifty_four_tables_and_fifty_six_columns_today()
    {
        // not a limit: the first two tests are the guard. This pins what the merge story was designed against, so a change is noticed in review.
        Assert.Equal((54, 56), (PatientOwnedData.Tables.Count, PatientOwnedData.All.Count));
    }
}
