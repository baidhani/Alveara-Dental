using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 S8 (U-022, owner decision 2026-11-22): the named exceptions to the merge registry (<see cref="PatientIdExceptions"/>) are complete and never overlap the registry. A column is either
/// registered (a merge moves it) or excepted, never both; each exception names a real patient-id column, states its merge handling and its export handling, and gives a reason; and an exception
/// excluded from export is in an excluded group of the export catalog and in no domain the package carries. Needs no database: it reads the model.
/// </summary>
public class PatientIdExceptionTests
{
    private static readonly IModel Model = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer("Server=.;Database=PatientIdExceptionModelOnly;Trusted_Connection=True").Options).Model;

    [Fact]
    public void No_column_is_both_registered_and_excepted_and_no_exception_is_declared_twice()
    {
        var registered = PatientOwnedData.All.Select(c => $"{c.Table}.{c.Column}").ToHashSet();
        var excepted = PatientIdExceptions.All.Select(e => $"{e.Table}.{e.Column}").ToList();
        Assert.Empty(excepted.Where(registered.Contains));
        Assert.Equal(excepted.Count, excepted.Distinct().Count());
        Assert.DoesNotContain(PatientIdExceptions.Tables, PatientOwnedData.Tables.Contains);                    // a table is merged or kept, not half of each
        Assert.DoesNotContain(PatientIdExceptions.Tables, PatientOwnedData.Bookkeeping.Contains);
    }

    [Fact]
    public void Every_exception_names_a_real_patient_id_column_and_states_its_merge_and_export_handling_and_a_reason()
    {
        Assert.NotEmpty(PatientIdExceptions.All);
        foreach (var e in PatientIdExceptions.All)
        {
            var entity = Model.GetEntityTypes().SingleOrDefault(t => t.GetTableName() == e.Table);
            Assert.True(entity is not null, $"{e.Table}: no such table in the model");
            var column = entity!.FindProperty(e.Column);
            Assert.True(column is not null && e.Column.EndsWith("PatientId", StringComparison.Ordinal), $"{e.Table}.{e.Column}: not a patient-id column of the model");
            Assert.True(Enum.IsDefined(e.Merge), $"{e.Table}.{e.Column}: no merge handling");
            Assert.True(Enum.IsDefined(e.Export), $"{e.Table}.{e.Column}: no export handling");
            Assert.False(string.IsNullOrWhiteSpace(e.Reason), $"{e.Table}.{e.Column}: an exception needs its reason");
            Assert.True(e.Reason.Trim().Length >= 20, $"{e.Table}.{e.Column}: the reason must say why in words");
        }
    }

    [Fact]
    public void An_exception_excluded_from_export_is_in_an_excluded_group_and_in_no_carried_domain()
    {
        foreach (var e in PatientIdExceptions.All.Where(x => x.Export == ExceptionExportHandling.ExcludeFromPatientExport))
        {
            Assert.Contains(e.Table, ExportDomainCatalog.ExcludedTables);
            Assert.DoesNotContain(e.Table, ExportDomainCatalog.IncludedTables);
            Assert.DoesNotContain(e.Table, ExportDomainCatalog.LookupTables);
        }
    }

    [Fact]
    public void The_record_opens_are_the_one_exception_today_with_the_owners_handling_and_reason()
    {
        var e = Assert.Single(PatientIdExceptions.All);
        Assert.Equal(("PatientRecordOpens", "PatientId", ExceptionMergeHandling.RetainOriginalRecordReference, ExceptionExportHandling.ExcludeFromPatientExport),
            (e.Table, e.Column, e.Merge, e.Export));
        Assert.Equal("Access history remains attached to the record that was opened.", e.Reason);
    }
}
