using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, how the package decides which rows of each table belong to the patients in scope, without a database: every table the catalog carries (in a domain or as a lookup) is reached by a rule, so no domain is
/// unavailable by construction; a child table is reached through its parent, a referred-to table through the rows that refer to it; the patients in scope appear in every condition and nothing else the caller could type
/// does; and rows are ordered by their key, so the same data gives the same file.
/// </summary>
public class ExportTableFiltersTests
{
    private const string Scope = "SELECT CONVERT(uniqueidentifier, '11111111-2222-3333-4444-555555555555')";
    private static readonly IModel Model = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer("Server=.;Database=ExportTableFiltersModelOnly;Trusted_Connection=True").Options).Model;
    private static readonly ExportTableFilters Filters = new(Model, Scope);

    [Fact]
    public void Every_table_the_package_carries_is_reached_by_a_rule()
    {
        var carried = ExportDomainCatalog.IncludedTables.Concat(ExportDomainCatalog.LookupTables).ToList();

        var unreached = carried.Where(t => Filters.WhereFor(t) is null).ToList();

        Assert.True(unreached.Count == 0, "No export rule reaches: " + string.Join(", ", unreached));
    }

    [Fact]
    public void The_patient_table_and_the_tables_that_hold_a_patient_id_are_selected_by_that_id()
    {
        Assert.Equal($"[Id] IN ({Scope})", Filters.WhereFor("Patients"));
        Assert.Equal($"[PatientId] IN ({Scope})", Filters.WhereFor("Appointments"));
        Assert.Equal($"[PatientId] IN ({Scope})", Filters.WhereFor("BillingEntries"));                      // what the patient owes, not what the patient paid for someone else
        Assert.Equal($"[RelatedPatientId] IN ({Scope})", Filters.WhereFor("Messages"));                     // a table that only points at a patient
    }

    [Fact]
    public void A_child_table_with_no_patient_id_is_selected_through_its_parent_and_a_referred_to_table_through_the_rows_that_refer_to_it()
    {
        var child = Filters.WhereFor("AppointmentEvents");
        var referred = Filters.WhereFor("Households");

        Assert.Contains("[AppointmentId] IN (SELECT [Id] FROM [Appointments] WHERE [PatientId] IN", child);
        Assert.Contains("IN (SELECT [HouseholdId] FROM [Patients] WHERE [Id] IN", referred);
    }

    [Fact]
    public void Every_condition_is_built_from_model_names_and_the_scope_alone()
    {
        var tables = ExportDomainCatalog.IncludedTables.Concat(ExportDomainCatalog.LookupTables).ToList();
        var names = Model.GetEntityTypes().SelectMany(e => e.GetProperties().Select(p => p.GetColumnName())).ToHashSet();

        foreach (var table in tables)
        {
            var where = Filters.WhereFor(table)!;
            Assert.DoesNotContain(";", where);
            Assert.DoesNotContain("--", where);
            Assert.DoesNotContain("'", where.Replace("'11111111-2222-3333-4444-555555555555'", ""));         // the only literal is the scope's GUID
            foreach (var column in System.Text.RegularExpressions.Regex.Matches(where, @"\[(\w+)\]").Select(m => m.Groups[1].Value))
                Assert.True(names.Contains(column) || Model.GetEntityTypes().Any(e => e.GetTableName() == column), $"{table}: '{column}' is neither a column nor a table of the model");
        }
    }

    [Fact]
    public void Rows_are_ordered_by_the_key_of_their_table_so_the_same_data_gives_the_same_file()
    {
        Assert.Equal(["Id"], Filters.OrderBy("Patients"));
        Assert.All(ExportDomainCatalog.IncludedTables.Concat(ExportDomainCatalog.LookupTables), t => Assert.NotEmpty(Filters.OrderBy(t)));
    }

    [Fact]
    public void A_scope_for_all_patients_changes_only_the_scope_text()
    {
        var all = new ExportTableFilters(Model, "SELECT [Id] FROM [Patients] WHERE [MergedAtUtc] IS NULL");

        Assert.Equal("[Id] IN (SELECT [Id] FROM [Patients] WHERE [MergedAtUtc] IS NULL)", all.WhereFor("Patients"));
        Assert.Equal(Filters.WhereFor("Appointments")!.Replace(Scope, "SELECT [Id] FROM [Patients] WHERE [MergedAtUtc] IS NULL"), all.WhereFor("Appointments"));
    }
}
