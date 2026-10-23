/**
 * ALV-N017 and ALV-N018: typed client for the permission policy (Controllers/PermissionsController, docs/PERMISSION_POLICY.md).
 *
 * - Reading: the effective matrix (where each cell comes from, who set it and why, the enabled users a role has, what needs a typed confirmation), the immutable history, the health of the stored rows.
 * - Writing: every write carries an `Idempotency-Key` (the same key for a retry of the same attempt) and the policy version the screen was showing; a stale caller gets 409 `policy_changed` and nothing is stored.
 * - Every refusal has a stable `error` code (see `policyErrorMessage`).
 */
import { ApiError, fetchCsrfToken, request } from "./authApi";

export type CellSource = "default" | "granted" | "revoked";

export interface EffectiveCell {
  permission: string;
  effective: boolean;
  source: CellSource;
  grantable: boolean;
  delegable: boolean;
  reason: string | null;
  changedByUserId: string | null;
  changedAtUtc: string | null;
  requiresTypedConfirmation: boolean;
}

export interface EffectiveRole { role: string; health: string; enabledUsers: number; permissions: EffectiveCell[] }
export interface InvalidRow { id: string; table: string; role: string; permission: string; targetRole: string | null; effect: string | null; reason: string; rowVersion: string }
export interface PolicyHealth {
  status: "Healthy" | "RepairRequired" | "Degraded";
  version: number;
  stale: boolean;
  invalidRowCount: number;
  retiredRowCount: number;
  overrideCount: number;
  rolesNeedingRepair: string[];
  invalidRows: InvalidRow[];
  retiredRows: { id: string; table: string; role: string; permission: string }[];
}
export interface EffectivePolicy { version: number; health: PolicyHealth; roles: EffectiveRole[] }

export interface HistoryItem {
  id: string; policyVersion: number; kind: string; role: string; permission: string; targetRole: string | null;
  oldEffective: boolean; newEffective: boolean; reason: string; changedByUserId: string; viaDelegation: boolean; changedAtUtc: string;
}
export interface HistoryPage { items: HistoryItem[]; total: number; page: number; pageSize: number }

export interface DelegationRow { delegateRole: string; permission: string; targetRole: string }
export interface DelegationTable { policyVersion: number; rows: DelegationRow[]; delegableDelegates: string[]; delegablePermissions: string[]; targetRoles: string[] }
export interface MyDelegations { policyVersion: number; delegations: { permission: string; targets: { role: string; allowed: boolean; enabledUsers: number }[] }[] }
export interface PolicyWriteResult { policyVersion: number; replayed: boolean; change: string; role: string | null; permission: string | null; effective: boolean | null; source: string | null; enabledUsers: number }

export interface SetOverrideInput { role: string; permission: string; effect: "Grant" | "Revoke" | "Revert"; reason: string; expectedVersion: number; confirmed: boolean; typedConfirmation?: string }
export interface DelegatedGrantInput { targetRole: string; permission: string; allowed: boolean; reason: string; expectedVersion: number; confirmed: boolean }
export interface SetDelegationsInput { rows: DelegationRow[]; reason: string; expectedVersion: number; confirmed: boolean }

export const newPolicyKey = (): string => (crypto.randomUUID ? crypto.randomUUID() : `k-${Date.now()}-${Math.random().toString(16).slice(2)}`);

async function write<T>(url: string, method: "PUT" | "POST", body: unknown, key: string): Promise<T> {
  const csrf = await fetchCsrfToken();
  return request<T>(url, { method, headers: { "Content-Type": "application/json", "X-CSRF-Token": csrf, "Idempotency-Key": key }, body: JSON.stringify(body) });
}

export const getEffective = () => request<EffectivePolicy>("/api/permissions/effective");
export const getPolicyHealth = () => request<PolicyHealth>("/api/permissions/health");
export const getPolicyHistory = (page = 1) => request<HistoryPage>(`/api/permissions/history?page=${page}&pageSize=25`);
export const getDelegations = () => request<DelegationTable>("/api/permissions/delegations");
export const getMyDelegations = () => request<MyDelegations>("/api/permissions/my-delegations");

export const setOverride = (input: SetOverrideInput, key: string) => write<PolicyWriteResult>("/api/permissions/overrides", "PUT", input, key);
export const delegatedGrant = (input: DelegatedGrantInput, key: string) => write<PolicyWriteResult>("/api/permissions/delegated-grants", "POST", input, key);
export const setDelegations = (input: SetDelegationsInput, key: string) => write<PolicyWriteResult>("/api/permissions/delegations", "PUT", input, key);
export const removeInvalidRow = async (id: string, reason: string, rowVersion: string) => {
  const csrf = await fetchCsrfToken();
  return request<{ id: string; policyVersion: number }>(`/api/permissions/invalid-rows/${id}/remove`, {
    method: "POST", headers: { "Content-Type": "application/json", "X-CSRF-Token": csrf }, body: JSON.stringify({ reason, rowVersion }),
  });
};

const MESSAGES: Record<string, string> = {
  policy_changed: "The policy changed since you opened this. Reload to see the current policy, then make the change again.",
  typed_confirmation_required: "Type the permission name exactly to confirm this change.",
  confirmation_required: "Confirm the change.",
  reason_required: "Write the reason for the change (up to 500 characters).",
  idempotency_key_reused: "That request was already used for a different change. Start the change again.",
  matches_default: "That is the default for this role. Use revert instead.",
  no_change: "Nothing to change: it is already set that way.",
  policy_needs_repair: "The stored policy for that role needs repair first (see the policy health panel).",
  permission_protected: "That permission can never be granted to another role.",
  role_not_editable: "The administrator and unassigned cells cannot be changed.",
  delegation_not_allowed: "You may not change that permission for that role.",
  policy_write_failed: "The change could not be saved, so nothing was changed. Try again.",
  row_changed: "That row changed since you looked at it. Reload and try again.",
  row_not_found: "That row is already gone.",
  row_not_removable: "Only an invalid row can be removed here.",
};

/** A message safe to show for a refused policy request. */
export function policyErrorMessage(err: unknown): string {
  if (err instanceof ApiError) return MESSAGES[err.code] ?? err.message ?? "That did not work. Try again.";
  return "That did not work. Check your connection and try again.";
}

export const isStaleWrite = (err: unknown) => err instanceof ApiError && err.code === "policy_changed";
