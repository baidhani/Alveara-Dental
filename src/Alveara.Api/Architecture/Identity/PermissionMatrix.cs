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
            Permission.ViewSchedule, Permission.ViewBilling),

        [Role.Hygienist] = Set(
            Permission.ViewPatientRecords, Permission.ManageClinicalNotes,
            Permission.ViewSchedule),

        [Role.Assistant] = Set(
            Permission.ViewPatientRecords, Permission.ViewSchedule),

        [Role.FrontDesk] = Set(
            Permission.ManageAppointments, Permission.ViewSchedule, Permission.ViewBilling,
            Permission.RegisterPatients),

        [Role.Billing] = Set(
            Permission.ViewBilling, Permission.ManageBilling, Permission.ViewPatientRecords),

        [Role.OfficeManager] = Set(
            Permission.ViewPatientRecords, Permission.ManageAppointments, Permission.ViewSchedule,
            Permission.ViewBilling, Permission.ManageBilling, Permission.ViewAuditLog, Permission.ViewPermissionMatrix,
            Permission.ManagePracticeConfiguration, // the "practice manager" of ALV-N003
            Permission.ViewBackupStatus, Permission.RegisterPatients),

        [Role.Unassigned] = Set(), // self-registered, not-yet-provisioned accounts hold no permissions at all
    };

    public static IReadOnlySet<Permission> PermissionsFor(Role role) =>
        RolePermissions.TryGetValue(role, out var permissions) ? permissions : Set();

    public static bool RoleHas(Role role, Permission permission) => PermissionsFor(role).Contains(permission);

    private static IReadOnlySet<Permission> Set(params IEnumerable<Permission> permissions) => permissions.ToHashSet();
}
