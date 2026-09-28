/**
 * Module registry for the shell's navigation.
 *
 * Authentication/RBAC do not exist yet (ALV-N001 is pre-auth). This registry
 * therefore lists every module unconditionally — it must NOT be read as "the
 * signed-in user can see all of these," because there is no signed-in user
 * concept yet. Permission-aware filtering is added by ALV-N009, which
 * consumes this same registry rather than replacing it.
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
];
