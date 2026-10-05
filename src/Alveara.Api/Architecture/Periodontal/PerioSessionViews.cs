namespace Alveara.Api.Architecture.Periodontal;

/// <summary>A tooth and site, used to name one entry to clear.</summary>
public sealed record PerioSiteRef(string ToothKey, string Site);

/// <summary>
/// One save into a draft session: sites to set or change, whole-tooth records to set or change, and sites or teeth to clear, applied together or not at all. A "tooth at a time" save is one batch
/// with that tooth's six sites and its grades. Leaving a list out leaves that kind of entry as it is.
/// </summary>
public sealed record PerioEntryBatch(IReadOnlyList<PerioReadingInput>? Readings, IReadOnlyList<PerioToothInput>? Teeth, IReadOnlyList<PerioSiteRef>? ClearSites, IReadOnlyList<string>? ClearTeeth);

/// <summary>
/// A chart session as the screen needs it. <see cref="RowVersion"/> is echoed back with each save so a stale edit is refused. <see cref="AbsentTeeth"/> are the teeth the odontogram records as missing:
/// they are left out of <see cref="Next"/> (the entry sweep) so keyboard entry never stops on them, and cannot be charted unless marked not charted. <see cref="Next"/> is the first site in the
/// documented sweep (<see cref="PerioSiteModel.Sweep"/>) that has no reading yet, or null when every chartable site has one.
/// </summary>
public sealed record PerioSessionView(
    Guid Id, Guid PatientId, string Status, DateTimeOffset StartedAtUtc, string StartedByName, DateTimeOffset? UpdatedAtUtc, string? UpdatedByName,
    DateTimeOffset? ClosedAtUtc, Guid? ExamId, string RowVersion,
    IReadOnlyList<PerioReadingView> Readings, IReadOnlyList<PerioToothView> Teeth, IReadOnlyList<string> AbsentTeeth, PerioSiteRef? Next, int ChartableSites);
