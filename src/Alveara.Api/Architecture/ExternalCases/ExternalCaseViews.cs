namespace Alveara.Api.Architecture.ExternalCases;

/// <summary>A refusal with a stable code a screen can act on and the HTTP status it maps to; the message is safe to show.</summary>
public sealed class ExternalCaseException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

// ---------- outside parties ----------

public sealed record CreatePartyRequest(string? Kind, string? Name, string? ContactDetails);
public sealed record UpdatePartyRequest(string? Name, string? ContactDetails, string? RowVersion);
public sealed record SetPartyActiveRequest(bool? Active, string? RowVersion);

public sealed record ExternalPartyView(Guid Id, string Kind, string Name, string? ContactDetails, bool IsActive, string RowVersion, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

// ---------- cases ----------

/// <summary>A new referral. The party is named by id; the due date is the practice-local date the practice will chase it (optional).</summary>
public sealed record CreateReferralRequest(Guid? PartyId, string? Reason, DateOnly? DueOn, Guid? EncounterId, string? Notes);

/// <summary>An edit replaces the editable fields as a whole and carries the version the person saw. The party can only change while the case is still a draft.</summary>
public sealed record UpdateReferralRequest(Guid? PartyId, string? Reason, DateOnly? DueOn, Guid? EncounterId, string? Notes, string? RowVersion);

public sealed record CreateLabRequest(Guid? PartyId, string? Title, DateOnly? DueOn, string? ToothKey, Guid? TreatmentPlanItemId, string? Material, string? Shade, string? Notes);
public sealed record UpdateLabRequest(Guid? PartyId, string? Title, DateOnly? DueOn, string? ToothKey, Guid? TreatmentPlanItemId, string? Material, string? Shade, string? Notes, string? RowVersion);

/// <summary>A move to another status. <see cref="Date"/> is the practice-local date it went out or came back (today when absent). A cancellation and a lab remake need a <see cref="Note"/>.</summary>
public sealed record ChangeStatusRequest(string? ToStatus, string? Note, DateOnly? Date, string? RowVersion);

/// <summary>One shape for both kinds so a list can mix them (<see cref="OpenChaseTaskId"/> is the open follow-up task chasing the case in the work queue, if there is one): <see cref="Summary"/> is the referral's reason or the lab case's title; the lab-only fields are null on a referral and the encounter is null on a lab case.</summary>
public sealed record ExternalCaseView(
    string Kind, Guid Id, Guid PatientId, Guid PartyId, string PartyName, bool PartyActive, string Summary, string Status, IReadOnlyList<string> NextStatuses,
    DateOnly? SentOn, DateOnly? ReceivedOn, DateOnly? DueOn, bool Overdue, Guid? EncounterId, string? ToothKey, Guid? TreatmentPlanItemId, string? Material, string? Shade, string? Notes,
    string? ClosureNote, DateTimeOffset? ClosedAtUtc, string RowVersion, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid? OpenChaseTaskId = null);

public sealed record ExternalCaseResult(ExternalCaseView Case, bool Replayed);

/// <summary>A document linked to a case. A link whose document can no longer be shown is flagged <see cref="Broken"/> with the reason, never hidden: the document is inactive, its file is missing or it failed the integrity check.</summary>
public sealed record CaseDocumentView(Guid DocumentId, string FileName, string Category, DateOnly DocumentDate, string? Description, string Status, bool Broken, string? BrokenReason, DateTimeOffset LinkedAtUtc, Guid LinkedByUserId);

public sealed record DocumentLinkResult(Guid DocumentId, bool Linked, bool Replayed);

public sealed record CaseEventView(Guid Id, string Kind, string? FromStatus, string? ToStatus, Guid? DocumentId, Guid ActorUserId, string? ActorName, DateTimeOffset OccurredAtUtc, string? Note);
