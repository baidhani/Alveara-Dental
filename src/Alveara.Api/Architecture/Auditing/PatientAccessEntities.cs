namespace Alveara.Api.Architecture.Auditing;

/// <summary>
/// ALV-N025 (PD-20, BR-01, SES-1): the audit-only lifecycle of one signed-in session. Written at sign-in with the same <c>SessionId</c> (the cookie's <c>sid</c> claim) and
/// the same fixed <c>ExpiresAtUtc</c> the cookie ticket carries; closed exactly once - by sign-out, by the expiry processor at the deadline, or by revocation - and the
/// transaction that closes it writes its one event. Nothing reads this to accept or refuse a request: authentication is the cookie and the security stamp, as before.
/// Guard 51301: a closed row, and the identifying columns of any row, never change; no row is deleted.
/// </summary>
public class AuditSession
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string? EndKind { get; set; }
    public bool EndAuditWritten { get; set; }
}

public static class AuditSessionEndKinds
{
    public const string SignedOut = "SignedOut";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";
}

/// <summary>ALV-N025 (PD-20): the first time a session opened a patient's record. The key (SessionId, PatientId) makes "once per patient per session"; append-only (51299).</summary>
public class PatientRecordOpen
{
    public Guid SessionId { get; set; }
    public Guid PatientId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset FirstOpenedAtUtc { get; set; }
}

/// <summary>ALV-N025 (PD-20) audit event types of the patient-access trail. Exports keep their own three events (EXP-2): PatientExported, PatientDataPackageExported, PatientReadableRecordExported.</summary>
public static class PatientAccessEvents
{
    public const string RecordOpened = "PatientRecordOpened";
    public const string DocumentDownloaded = "PatientDocumentDownloaded";
    public const string MaterialPrinted = "PatientMaterialPrinted";
    public const string UserSignedOut = "UserSignedOut";
    public const string SessionExpired = "SessionExpired";
    public const string SessionRevoked = "SessionRevoked";
}
