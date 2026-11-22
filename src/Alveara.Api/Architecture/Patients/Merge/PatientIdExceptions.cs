namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>What a merge does with an excepted patient-id column.</summary>
public enum ExceptionMergeHandling
{
    /// <summary>A merge never moves the row: it keeps pointing at the record it was written about, merged away or not.</summary>
    RetainOriginalRecordReference,
}

/// <summary>What a patient export does with an excepted patient-id column's table.</summary>
public enum ExceptionExportHandling
{
    /// <summary>The table is in an excluded group of <see cref="Portability.Package.ExportDomainCatalog"/> and never in a patient's package.</summary>
    ExcludeFromPatientExport,
}

/// <summary>One deliberate exception: a column that holds a patient id but is NOT patient-owned data, with how a merge and an export treat it and why.</summary>
public sealed record PatientIdException(string Table, string Column, ExceptionMergeHandling Merge, ExceptionExportHandling Export, string Reason);

/// <summary>
/// ALV-N026 S8 (U-022, owner decision 2026-11-22): the named exceptions to <see cref="PatientOwnedData"/>. Every model column that holds a patient id is either registered there (a merge moves it)
/// or declared here with its merge handling, its export handling and a reason, never silently absent from either list (<c>PatientOwnedDataTests</c> and <c>ExportDomainCatalogTests</c> check both
/// ways, and <c>PatientIdExceptionTests</c> that no column is both registered and excepted and that each exception is complete).
/// </summary>
public static class PatientIdExceptions
{
    public static readonly IReadOnlyList<PatientIdException> All =
    [
        // ALV-N025 (PD-20): who first opened which record in a session. Access/audit history, not current clinical or business data: moving it in a merge would rewrite the audit trail.
        new("PatientRecordOpens", "PatientId", ExceptionMergeHandling.RetainOriginalRecordReference, ExceptionExportHandling.ExcludeFromPatientExport,
            "Access history remains attached to the record that was opened."),
    ];

    /// <summary>The excepted tables.</summary>
    public static IReadOnlyList<string> Tables => All.Select(e => e.Table).Distinct().Order().ToList();
}
