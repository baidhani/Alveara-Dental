using Alveara.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-N030-C01 (brief 18.7, R08-3; approved R10): records - and never repairs - an active Practice Owner who breaks the owner invariant (an inactive profile, no
/// login, a disabled login, a role other than Administrator, or an account that is not a staff account). A migration must not grant authority, so nothing is
/// promoted; an Administrator fixes it (role change to Administrator, re-enabling, or a transfer) and the dashboard says how (<see cref="StatusAsync"/>).
/// <para>Idempotency: the audit row's primary key is the first 16 bytes of SHA-256 over the UTF-8 state fingerprint
/// <c>alveara.owner-invariant-violation:v1:{designationId:N}:{staffProfileId:N}:{staffIsActive 0|1}:{userAccountId:N|none}:{accountIsDisabled 0|1|none}:{accountRole name|none}:{accountKind name|none}</c>,
/// and the row is inserted only <c>WHERE NOT EXISTS</c> a row with that id (locked). The same state run twice adds nothing; a different violation state adds one
/// new row; earlier rows are never changed. The migration (<c>RecordPracticeOwnerInvariantViolations</c>) and <see cref="RecordIfNewAsync"/> run this ONE statement.</para>
/// </summary>
public static class OwnerInvariantAudit
{
    public const string FingerprintPrefix = "alveara.owner-invariant-violation:v1:";

    /// <summary>The single statement. All literals are ASCII, so the varchar bytes hashed are the UTF-8 bytes of the documented fingerprint.</summary>
    public const string Sql = """
        WITH v AS (
            SELECT s.[Id] AS [StaffId],
                CONCAT('alveara.owner-invariant-violation:v1:',
                    LOWER(REPLACE(CONVERT(varchar(36), d.[Id]), '-', '')), ':',
                    LOWER(REPLACE(CONVERT(varchar(36), s.[Id]), '-', '')), ':',
                    CASE WHEN s.[IsActive] = 1 THEN '1' ELSE '0' END, ':',
                    COALESCE(LOWER(REPLACE(CONVERT(varchar(36), u.[Id]), '-', '')), 'none'), ':',
                    CASE WHEN u.[Id] IS NULL THEN 'none' WHEN u.[IsDisabled] = 1 THEN '1' ELSE '0' END, ':',
                    CASE WHEN u.[Id] IS NULL THEN 'none' ELSE CASE u.[Role] WHEN 0 THEN 'Dentist' WHEN 1 THEN 'Hygienist' WHEN 2 THEN 'Assistant' WHEN 3 THEN 'FrontDesk'
                        WHEN 4 THEN 'Billing' WHEN 5 THEN 'OfficeManager' WHEN 6 THEN 'Admin' WHEN 7 THEN 'Unassigned' ELSE CONVERT(varchar(11), u.[Role]) END END, ':',
                    CASE WHEN u.[Id] IS NULL THEN 'none' WHEN u.[AccountKind] = 0 THEN 'Staff' WHEN u.[AccountKind] = 1 THEN 'DeploymentAdministrator'
                        ELSE CONVERT(varchar(4), u.[AccountKind]) END) AS [Fingerprint],
                CONCAT_WS(',',
                    CASE WHEN s.[IsActive] = 0 THEN 'staffInactive' END,
                    CASE WHEN u.[Id] IS NULL THEN 'noLogin' END,
                    CASE WHEN u.[IsDisabled] = 1 THEN 'loginDisabled' END,
                    CASE WHEN u.[Id] IS NOT NULL AND u.[Role] <> 6 THEN 'notAdministrator' END,
                    CASE WHEN u.[Id] IS NOT NULL AND u.[AccountKind] <> 0 THEN 'notStaffAccount' END) AS [Failing]
            FROM [PracticeOwnerDesignations] d
            JOIN [StaffProfiles] s ON s.[Id] = d.[StaffProfileId]
            LEFT JOIN [UserAccounts] u ON u.[Id] = s.[UserAccountId]
            WHERE d.[EndedAtUtc] IS NULL),
        w AS (
            SELECT [StaffId], [Failing], CAST(SUBSTRING(HASHBYTES('SHA2_256', CAST([Fingerprint] AS varchar(400))), 1, 16) AS uniqueidentifier) AS [AuditId]
            FROM v WHERE [Failing] <> '')
        INSERT INTO [AuditLogEntries] ([Id], [EventType], [TargetUserAccountId], [PerformedByUserAccountId], [Details], [TimestampUtc], [EntityType], [Reason], [CorrelationId])
        SELECT w.[AuditId], 'PracticeOwnerInvariantViolation', w.[StaffId], NULL,
            CONCAT('staffProfileId=', LOWER(CONVERT(varchar(36), w.[StaffId])), '; failing=', w.[Failing], '; actor=system (upgrade)'),
            SYSDATETIMEOFFSET(), 'StaffProfile', NULL, NULL
        FROM w
        WHERE NOT EXISTS (SELECT 1 FROM [AuditLogEntries] a WITH (UPDLOCK, HOLDLOCK) WHERE a.[Id] = w.[AuditId]);
        """;

    /// <summary>Runs the statement (the same one the migration runs); returns the number of rows written (0 or 1).</summary>
    public static Task<int> RecordIfNewAsync(AlveraDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(Sql, ct);

    /// <summary>
    /// For the signed-in client (brief 18.7): <c>none</c> (no active designation), <c>valid</c> (the owner invariant holds) or <c>degraded</c> (it does not; the
    /// dashboard tells an Administrator which condition fails and how to fix it).
    /// </summary>
    public static async Task<string> StatusAsync(AlveraDbContext db, CancellationToken ct)
    {
        var row = await db.PracticeOwnerDesignations.AsNoTracking().Where(d => d.EndedAtUtc == null)
            .Select(d => new
            {
                d.StaffProfile!.IsActive,
                Disabled = d.StaffProfile.UserAccount == null ? (bool?)null : d.StaffProfile.UserAccount.IsDisabled,
                Role = d.StaffProfile.UserAccount == null ? (Role?)null : d.StaffProfile.UserAccount.Role,
                Kind = d.StaffProfile.UserAccount == null ? (AccountKind?)null : d.StaffProfile.UserAccount.AccountKind,
            }).SingleOrDefaultAsync(ct);
        if (row is null) return "none";
        return row.IsActive && row.Disabled == false && row.Role == Role.Admin && row.Kind == AccountKind.Staff ? "valid" : "degraded";
    }
}
