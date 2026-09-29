/**
 * Module registry for the shell's navigation.
 *
 * ALV-001-C01 adds real authentication/RBAC, but permission-aware filtering of this list is
 * explicitly ALV-N009's job (it consumes this same registry rather than replacing it) - so this
 * still lists every module unconditionally, including Security Administration. The page itself
 * (AdminUsersPage) enforces its own permission check and shows PermissionDenied rather than
 * relying on the nav to hide it, so this is not a security gap in the meantime, just an
 * unpolished nav for a caller who can't use what they clicked.
 */
export interface ModuleDefinition {
  id: string;
  label: string;
  path: string;
}

export const moduleRegistry: ModuleDefinition[] = [
  { id: "dashboard", label: "Dashboard", path: "/" },
  { id: "showcase", label: "Component Showcase", path: "/showcase" },
  { id: "system-status", label: "System Status", path: "/system-status" },
  { id: "admin-users", label: "Security Administration", path: "/admin/users" },
  { id: "admin-permissions", label: "Permission Matrix", path: "/admin/permissions" },
  { id: "mfa-settings", label: "Multi-Factor Authentication", path: "/settings/mfa" },
];
