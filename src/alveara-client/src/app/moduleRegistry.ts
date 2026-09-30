/**
 * Module registry for the shell's navigation.
 *
 * ALV-N009: nav entries are filtered by `requiredPermission` (AppShell, via
 * useAuth().hasPermission()) - an entry with no `requiredPermission` is visible to every signed-in
 * user; hiding a nav link is a UX courtesy only, never the access-control decision itself, which
 * the server always re-checks via [RequirePermission] regardless of what the client shows or
 * hides (see RouteGuards.tsx's RequirePermission, which also re-derives this same check at the
 * route level so a caller can't reach a page by typing its URL even if the link is hidden).
 */
export interface ModuleDefinition {
  id: string;
  label: string;
  path: string;
  requiredPermission?: string;
}

export const moduleRegistry: ModuleDefinition[] = [
  { id: "dashboard", label: "Dashboard", path: "/" },
  { id: "showcase", label: "Component Showcase", path: "/showcase" },
  { id: "system-status", label: "System Status", path: "/system-status" },
  { id: "admin-users", label: "Security Administration", path: "/admin/users", requiredPermission: "ManageUsers" },
  { id: "admin-permissions", label: "Permission Matrix", path: "/admin/permissions", requiredPermission: "ViewPermissionMatrix" },
  { id: "mfa-settings", label: "Multi-Factor Authentication", path: "/settings/mfa" },
];
