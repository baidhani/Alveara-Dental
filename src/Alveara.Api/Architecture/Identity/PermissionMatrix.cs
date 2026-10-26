namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// The authoritative role -> permission mapping. This is the single place that decides what each
/// of the story's named roles can do; <see cref="RequirePermissionAttribute"/> and the
/// <c>GET /api/auth/permissions</c> / <c>GET /api/auth/permission-matrix</c> endpoints both read
/// from here rather than encoding their own logic, so there is exactly one place to audit or
/// change a role's grants.
/// </summary>
public static class PermissionMatrix
{
    private static readonly IReadOnlyDictionary<Role, IReadOnlySet<Permission>> RolePermissions = new Dictionary<Role, IReadOnlySet<Permission>>
    {
        [Role.Admin] = Set(Enum.GetValues<Permission>()), // administers everything, including security administration itself

        [Role.Dentist] = Set(
            Permission.ViewPatientRecords, Permission.ManageClinicalNotes, Permission.ManageTreatmentPlans,
            Permission.ViewSchedule, Permission.ViewBilling,
            Permission.CompleteForms, Permission.ViewSignedForms, // ALV-N010
            Permission.UpdateChairsideFlow, // ALV-011-C01
            Permission.ViewClinicalDocumentation, // STORY-005
            Permission.ManageClinicalTemplates, // ALV-005-C01
            Permission.ViewSafetyIndicator, // ALV-N011
            Permission.ManagePrescriptions, Permission.ViewPrescriptions, // STORY-016: prescribes
            Permission.ViewDocuments, // STORY-010: reads the library
            Permission.SendMessages, // ALV-N015
            Permission.ViewFollowUps, Permission.ManageFollowUps, // STORY-009
            Permission.ViewExternalCases, Permission.ManageExternalCases), // ALV-009-C02: refers and orders lab work

        [Role.Hygienist] = Set(
            Permission.ViewPatientRecords, Permission.ManageClinicalNotes,
            Permission.ViewSchedule, Permission.CompleteForms, Permission.ViewSignedForms,
            Permission.UpdateChairsideFlow, // ALV-011-C01
            Permission.ViewClinicalDocumentation, // STORY-005
            Permission.ViewSafetyIndicator, // ALV-N011
            Permission.ViewPrescriptions, // STORY-016: reads, cannot prescribe
            Permission.ViewDocuments, // STORY-010
            Permission.SendMessages, // ALV-N015
            Permission.ViewFollowUps, Permission.ManageFollowUps, // STORY-009
            Permission.ViewExternalCases), // ALV-009-C02: reads referral and lab cases

        [Role.Assistant] = Set(
            Permission.ViewPatientRecords, Permission.ViewSchedule, Permission.CompleteForms, Permission.ViewSignedForms,
            Permission.UpdateChairsideFlow, // ALV-011-C01
            Permission.ViewClinicalDocumentation, // STORY-005: reads the chart, cannot write it
            Permission.ViewSafetyIndicator, // ALV-N011
            Permission.ViewPrescriptions, // STORY-016: reads, cannot prescribe
            Permission.ViewDocuments, // STORY-010
            Permission.SendMessages, // ALV-N015
            Permission.ViewFollowUps, Permission.ManageFollowUps, // STORY-009
            Permission.ViewExternalCases), // ALV-009-C02: reads referral and lab cases

        [Role.FrontDesk] = Set(
            Permission.ManageAppointments, Permission.ViewSchedule, Permission.ViewBilling,
            Permission.RegisterPatients, Permission.EditPatients,
            Permission.ViewPatientRecords, // ALV-003-C01: front desk must find and open the patients it registers
            Permission.CompleteForms, Permission.ViewSignedForms, // ALV-N010: forms are completed at the front desk
            Permission.UpdateVisitFlow, // ALV-011-C01: confirm, check in and check out
            Permission.ViewDocuments, // STORY-010: reads the library, cannot import
            Permission.SendMessages, // ALV-N015
            Permission.ViewFollowUps, Permission.ManageFollowUps, // STORY-009
            Permission.ViewExternalCases, Permission.ManageExternalCases), // ALV-009-C02: tracks referral and lab cases

        [Role.Billing] = Set(
            Permission.ViewBilling, Permission.ManageBilling, Permission.ViewPatientRecords,
            Permission.ViewSignedForms, // ALV-N010: read-only (financial forms)
            Permission.ViewDocuments, // STORY-010
            Permission.SendMessages), // ALV-N015

        [Role.OfficeManager] = Set(
            Permission.ViewPatientRecords, Permission.ManageAppointments, Permission.ViewSchedule,
            Permission.ViewBilling, Permission.ManageBilling, Permission.ViewAuditLog, Permission.ViewPermissionMatrix,
            Permission.ManagePracticeConfiguration, // the "practice manager" of ALV-N003
            Permission.ViewBackupStatus, Permission.RegisterPatients, Permission.EditPatients,
            Permission.ManageFormTemplates, Permission.CompleteForms, Permission.ViewSignedForms, Permission.VoidForms, // ALV-N010
            Permission.UpdateVisitFlow, Permission.UpdateChairsideFlow, // ALV-011-C01: the practice manager covers both teams
            Permission.ImportDocuments, Permission.ViewDocuments, // STORY-010: imports and categorizes; the settings stay with the administrator
            Permission.ManageDocumentStorage, // ALV-010-C01: checks the stored files and reviews the ones nothing points to
            Permission.SendMessages, Permission.ReceiveDocumentNotifications, // ALV-N015: sends messages; is told when a duplicate document is imported
            Permission.ViewFollowUps, Permission.ManageFollowUps, // STORY-009
            Permission.ManageRecallSettings, // ALV-009-C01: the practice's recall settings
            Permission.ViewExternalCases, Permission.ManageExternalCases, Permission.ManageExternalParties, // ALV-009-C02
            Permission.ImportPatientData), // STORY-018: brings a patient in from a file (exporting one stays with the administrator)

        [Role.Unassigned] = Set(), // self-registered, not-yet-provisioned accounts hold no permissions at all
    };

    public static IReadOnlySet<Permission> PermissionsFor(Role role) =>
        RolePermissions.TryGetValue(role, out var permissions) ? permissions : Set();

    public static bool RoleHas(Role role, Permission permission) => PermissionsFor(role).Contains(permission);

    private static IReadOnlySet<Permission> Set(params IEnumerable<Permission> permissions) => permissions.ToHashSet();
}
