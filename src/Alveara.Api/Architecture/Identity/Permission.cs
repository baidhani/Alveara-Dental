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

    // ALV-N011: the MINIMAL patient-safety indicator on the live visit board - whether a patient has an active safety alert or an open clearance, and nothing else (no diagnosis,
    // allergy or medication). Held by the clinical team (dentist, hygienist, assistant) and the administrator; front desk, billing and the practice manager do not hold it - they still see the board, just not this. Reading the full safety context uses
    // ViewClinicalDocumentation and changing it ManageClinicalNotes. Appended last so existing ordinals are undisturbed.
    ViewSafetyIndicator,

    // STORY-016: prescribing. Writing a prescription is a prescriber's act: held by the dentist and the administrator only. Reading a patient's prescriptions follows clinical documentation (dentist,
    // hygienist, assistant, administrator). Front desk, billing and the practice manager hold neither. Appended last so existing ordinals are undisturbed.
    ManagePrescriptions,
    ViewPrescriptions,

    // STORY-010: imported patient documents. Importing, recategorizing and editing a document's details is the administrator's and the office manager's; reading the library follows the staff who work with a
    // patient's file (administrator, office manager, front desk, dentist, hygienist, assistant, billing); the size limit, the categories and the blocked file types are the administrator's. Appended last so existing
    // ordinals are undisturbed.
    ImportDocuments,
    ViewDocuments,
    ManageDocumentSettings,

    // ALV-010-C01: checking that the stored files of documents are present and intact, and reviewing and removing stored files that no document points to. Held by the administrator and the office manager.
    ManageDocumentStorage,

    // ALV-N015: notifications and operational messages. Reading, marking read and hiding one's own notifications and messages needs no permission (every signed-in person has a bell). Sending a message is held by
    // every working role; the administrator also holds it. Being a recipient of the duplicate-document notification is the administrator's and the office manager's. Seeing the notification history of everyone
    // (metadata only, never message text) and opening one chosen message through the audited break-glass action are the administrator's alone. Appended last so existing ordinals are undisturbed.
    SendMessages,
    ReceiveDocumentNotifications,
    ViewNotificationAuditHistory,
    ViewMessageAuditContent,

    // ALV-N017: the right to change which roles hold which permissions (the policy write path arrives in ALV-N018; this story only defines the permission). It is protected: held by the administrator
    // alone, it can never be granted to another role or delegated. Appended last so existing ordinals are undisturbed.
    ManagePermissionGrants,

    // STORY-009: follow-up tasks, recall intervals and reminders. Viewing a patient's follow-up tasks and their history, and managing them (create, assign, edit, complete, cancel), are separate so a role can be given one without the other.
    // Held by the dentist, hygienist, assistant, front desk and office manager (and the administrator); billing holds neither. Neither is protected: both are grantable and delegable under the policy rules. Appended last so existing ordinals are undisturbed.
    ViewFollowUps,
    ManageFollowUps,

    // ALV-009-C01: the practice's recall settings (the default recall interval, how far ahead a recall task is made, which patients are still recalled). Held by the administrator and the office manager; changing one patient's
    // own recall interval needs only ManageFollowUps. Not protected: grantable and delegable like the follow-up permissions. Appended last so existing ordinals are undisturbed.
    ManageRecallSettings,

    // ALV-009-C02: referrals and dental laboratory cases. Viewing the cases and their timeline (held like the follow-up view: dentist, hygienist, assistant, front desk, office manager), managing them
    // (create, send, receive, complete, cancel, link documents: dentist, front desk, office manager) and maintaining the list of outside parties (referral providers, lab vendors: office manager) are
    // separate. None is protected: all are grantable and delegable. A case's linked documents still need ViewDocuments. Appended last so existing ordinals are undisturbed.
    ViewExternalCases,
    ManageExternalCases,
    ManageExternalParties,
}
