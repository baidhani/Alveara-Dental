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

    // STORY-003: registering patients (demographics + contact). Appended last so existing ordinals are undisturbed.
    RegisterPatients,

    // ALV-003-C01: editing patient details, household/guarantor links and active state.
    EditPatients,

    // ALV-N010: versioned forms, consents and signatures. Appended last so existing ordinals are undisturbed.
    ManageFormTemplates,
    CompleteForms,
    ViewSignedForms,
    VoidForms,

    // ALV-011-C01: moving a patient through the visit. Front-office moves (confirm, check in, check out) and chairside moves (ready, seat, start treatment,
    // complete) are separate so each team does its own work; either may reassign the visit's provider/operatory. Appended last so ordinals are undisturbed.
    // STORY-011's own endpoints keep ManageAppointments.
    UpdateVisitFlow,
    UpdateChairsideFlow,

    // STORY-005: reading a patient's clinical documentation (medical and dental history, allergies, medications, encounter notes and their addenda). Separate from
    // ViewPatientRecords on purpose: that permission is held by front desk and billing for demographics, and must not open a patient's medical history to them.
    // Writing stays with ManageClinicalNotes. Appended last so existing ordinals are undisturbed.
    ViewClinicalDocumentation,

    // ALV-005-C01: configuring the clinical note templates (which note sections a template has, which are required before signing). Owned by the clinical-documentation
    // domain, not generic practice configuration; held by the dentist and the administrator. Using a template stays with ManageClinicalNotes. Appended last.
    ManageClinicalTemplates,
}
