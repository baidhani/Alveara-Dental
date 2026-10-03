namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// STORY-005: one clinical encounter for a patient - the unit in which medical and dental history, allergies and medications are documented.
///
/// An encounter is a Draft while it is being documented and Finalized once the clinician finalizes it. A finalized encounter is never changed again: the
/// application refuses it, and the database refuses it independently (triggers), so a bug or a hand-written update cannot rewrite the chart. The only thing
/// that can happen to a finalized encounter is an <see cref="EncounterAddendum"/>, which is appended beside the original and leaves it untouched.
///
/// An encounter may be linked to the appointment it documents (at most one encounter per appointment), but does not need one: a note can be written
/// without a booking. Clinicians sign by finalizing; the finalizer is recorded. Nothing here is a billing or treatment-plan record.
/// </summary>
public class Encounter
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>The appointment this encounter documents, when there is one (unique: an appointment has at most one encounter).</summary>
    public Guid? AppointmentId { get; set; }
    /// <summary>When the encounter took place (UTC instant); the clinician enters it, it is not inferred from when the note was typed.</summary>
    public DateTimeOffset EncounterAtUtc { get; set; }
    /// <summary>The caller's Idempotency-Key for the request that started this encounter (unique), so a retried start returns it instead of creating a second one.</summary>
    public string? StartKey { get; set; }
    /// <summary><see cref="EncounterStatuses.Draft"/> or <see cref="EncounterStatuses.Finalized"/>.</summary>
    public required string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    /// <summary>Set exactly when Status is Finalized (a database check keeps the two in step).</summary>
    public DateTimeOffset? FinalizedAtUtc { get; set; }
    public Guid? FinalizedByUserId { get; set; }
    /// <summary>Optimistic concurrency: every change to the encounter or its entries moves this, so a stale editor is refused instead of overwriting.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One structured item documented in an encounter: a medical-history condition, a dental-history item, an allergy or a medication. The columns that do not
/// apply to a kind stay null (an allergy has a reaction and severity, a medication a dose and frequency). A removed entry is kept (RemovedAtUtc) so what was
/// typed is never lost; it is simply not shown or counted. Entries can only change while the encounter is a Draft.
/// </summary>
public class EncounterEntry
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    /// <summary>One of <see cref="EncounterEntryKinds"/>.</summary>
    public required string Kind { get; set; }
    /// <summary>The condition, history item, allergen or medication name (required).</summary>
    public required string Name { get; set; }
    /// <summary>Free-text detail (history items; any kind).</summary>
    public string? Detail { get; set; }
    /// <summary>Allergies: the reaction, when known.</summary>
    public string? Reaction { get; set; }
    /// <summary>Allergies: one of <see cref="EncounterSeverities"/>, when known.</summary>
    public string? Severity { get; set; }
    /// <summary>Medications: dose and frequency as the clinician wrote them.</summary>
    public string? Dose { get; set; }
    public string? Frequency { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
    public Guid? RemovedByUserId { get; set; }
}

/// <summary>
/// "Reviewed - none reported" for one section of an encounter, so a clinician is never forced to invent an item to complete the documentation. At most one
/// mark per section per encounter; a section with a mark has no entries (the service enforces it). Only <see cref="EncounterSectionStates.NoneReported"/>
/// exists; the column allows later states (unknown, not reviewed) without changing what this one means.
/// </summary>
public class EncounterSectionMark
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    /// <summary>One of <see cref="EncounterEntryKinds"/> (the section is named by the kind of entry it holds).</summary>
    public required string Section { get; set; }
    public required string State { get; set; }
    public DateTimeOffset MarkedAtUtc { get; set; }
    public Guid? MarkedByUserId { get; set; }
}

/// <summary>
/// An addendum to a FINALIZED encounter: text appended beside the original, which it never changes. Append-only (a database trigger refuses any update or
/// delete) and only insertable once the encounter is Finalized. The client key makes a retried submit return the same addendum instead of adding a second.
/// </summary>
public class EncounterAddendum
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    public required string Text { get; set; }
    /// <summary>The caller's idempotency key for this addendum (unique per encounter).</summary>
    public required string ClientKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// Append-only history of an encounter: created, entry added/changed/removed, section marked/unmarked, finalized, addendum added. Written in the same save as
/// the change it describes, so the change and its history stand or fall together. The detail never contains clinical text (names, reactions, doses, notes).
/// </summary>
public class EncounterEvent
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    public Guid PatientId { get; set; }
    public required string EventType { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    /// <summary>A short, PHI-free description (for example "Allergy entry added").</summary>
    public string? Detail { get; set; }
}

public static class EncounterStatuses
{
    public const string Draft = "Draft";
    public const string Finalized = "Finalized";
    public static readonly IReadOnlyList<string> All = [Draft, Finalized];
}

public static class EncounterEntryKinds
{
    public const string MedicalHistory = "MedicalHistory";
    public const string DentalHistory = "DentalHistory";
    public const string Allergy = "Allergy";
    public const string Medication = "Medication";
    /// <summary>The four sections of an encounter's documentation, in the order they are presented.</summary>
    public static readonly IReadOnlyList<string> All = [MedicalHistory, DentalHistory, Allergy, Medication];
}

public static class EncounterSeverities
{
    public const string Mild = "Mild";
    public const string Moderate = "Moderate";
    public const string Severe = "Severe";
    public static readonly IReadOnlyList<string> All = [Mild, Moderate, Severe];
}

public static class EncounterSectionStates
{
    public const string NoneReported = "NoneReported";
}

public static class EncounterEventTypes
{
    public const string Created = "Created";
    public const string EntryAdded = "EntryAdded";
    public const string EntryChanged = "EntryChanged";
    public const string EntryRemoved = "EntryRemoved";
    public const string SectionMarked = "SectionMarked";
    public const string SectionUnmarked = "SectionUnmarked";
    public const string Finalized = "Finalized";
    public const string AddendumAdded = "AddendumAdded";
}
