namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-001-C01's "granular permission matrix mapped to dentist, hygienist, assistant, front
/// desk, billing, office manager, admin". Named permissions rather than a single role check, so
/// authorization decisions describe what a caller may DO, not just which role they hold - future
/// modules (clinical notes, billing, scheduling) gate on these same permissions instead of adding
/// their own ad hoc role checks. See <see cref="PermissionMatrix"/> for the role -> permission
/// mapping and <see cref="RequirePermissionAttribute"/> for the enforcement point.
/// </summary>
public enum Permission
{
    // Security administration (identity module - the only module this release actually ships).
    ManageUsers,
    ManageRoles,
    ManageAccountStatus,
    ManageMfaPolicy,
    IssuePasswordResets,
    RevokeSessions,
    ViewAuditLog,
    ViewPermissionMatrix,

    // Clinical (named now so the matrix is genuinely role-shaped per the story's role list, even
    // though no clinical module exists yet to enforce against - a future module authorizes
    // against these same permissions rather than re-deriving its own role table).
    ViewPatientRecords,
    ManageClinicalNotes,
    ManageTreatmentPlans,

    // Front-office / scheduling.
    ManageAppointments,
    ViewSchedule,

    // Billing.
    ViewBilling,
    ManageBilling,

    // ALV-N003: practice, staff, provider, operatory, appointment-type, and availability
    // configuration. Appended last so existing members' ordinal values are undisturbed.
    ManagePracticeConfiguration,

    // ALV-N004: backup and recovery. Managing backups (settings, recovery key, manual backup,
    // verification, restore drills) is administrator-only; seeing backup status/failures is also
    // granted to the practice manager so a failed backup cannot go unnoticed.
    ManageBackups,
    ViewBackupStatus,
}
