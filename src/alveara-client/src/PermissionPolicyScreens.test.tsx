import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { PermissionMatrixPage, MATRIX_REFRESH_MS } from "./pages/PermissionMatrixPage";

// ALV-N018 screens against a small in-memory server that answers like the real API (stale check, stable error codes, idempotency key and body recorded).

let perms = new Set<string>();
vi.mock("./contexts/AuthContext", () => ({ useAuth: () => ({ hasPermission: (p: string) => perms.has(p) }) }));

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const PROTECTED = ["ManageUsers", "ManageRoles", "ManageMfaPolicy", "IssuePasswordResets", "ManagePermissionGrants"];

type Server = {
  version: number; invalid: unknown[]; delegations: { delegateRole: string; permission: string; targetRole: string }[];
  my: { permission: string; targets: { role: string; allowed: boolean; enabledUsers: number }[] }[]; calls: { method: string; url: string; headers: Record<string, string>; body: any }[];
  reply: (call: { method: string; url: string; body: any }) => Response | null; effectiveCalls: number;
};
let s: Server;

function cell(permission: string, role: string, effective: boolean, source = "default") {
  const fixed = role === "Admin" || role === "Unassigned";
  return { permission, effective, source, grantable: !PROTECTED.includes(permission) && !fixed, delegable: true, reason: source === "default" ? null : "Because", changedByUserId: null, changedAtUtc: null, requiresTypedConfirmation: ["ViewPatientRecords", "ManageAccountStatus"].includes(permission) };
}

function effective() {
  s.effectiveCalls++;
  const names = ["ManageUsers", "ViewBilling", "ViewPatientRecords", "ImportDocuments"];
  const roles = ["Admin", "Dentist", "Unassigned"].map((role) => ({
    role, health: "Healthy", enabledUsers: role === "Dentist" ? 3 : 1,
    permissions: names.map((p) => cell(p, role, role === "Admin" || (role === "Dentist" && p !== "ManageUsers" && p !== "ImportDocuments"), role === "Dentist" && p === "ImportDocuments" ? "granted" : "default")),
  }));
  const health = { status: s.invalid.length ? "RepairRequired" : "Healthy", version: s.version, stale: false, invalidRowCount: s.invalid.length, retiredRowCount: 0, overrideCount: 1, rolesNeedingRepair: s.invalid.length ? ["Dentist"] : [], invalidRows: s.invalid, retiredRows: [] };
  return { version: s.version, health, roles };
}

beforeEach(() => {
  perms = new Set(["ViewPermissionMatrix", "ManagePermissionGrants", "ViewAuditLog"]);
  s = { version: 7, invalid: [], delegations: [{ delegateRole: "OfficeManager", permission: "ImportDocuments", targetRole: "Dentist" }], my: [], calls: [], reply: () => null, effectiveCalls: 0 };
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url); const method = init?.method ?? "GET";
    const headers = (init?.headers ?? {}) as Record<string, string>;
    const body = init?.body ? JSON.parse(String(init.body)) : undefined;
    if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
    s.calls.push({ method, url: u, headers, body });
    const custom = s.reply({ method, url: u, body });
    if (custom) return Promise.resolve(custom);
    if (u === "/api/permissions/effective") return Promise.resolve(json(effective()));
    if (u === "/api/auth/permission-matrix") return Promise.resolve(json([{ role: "Admin", permissions: ["ViewBilling"] }, { role: "Dentist", permissions: ["ViewBilling"] }]));
    if (u === "/api/permissions/my-delegations") return Promise.resolve(json({ policyVersion: s.version, delegations: s.my }));
    if (u === "/api/permissions/delegations" && method === "GET") return Promise.resolve(json({ policyVersion: s.version, rows: s.delegations, delegableDelegates: ["OfficeManager"], delegablePermissions: ["ImportDocuments", "ViewDocuments"], targetRoles: ["Dentist", "Hygienist"] }));
    if (u.startsWith("/api/permissions/history")) return Promise.resolve(json({ items: [{ id: "h1", policyVersion: 6, kind: "Revoked", role: "Dentist", permission: "ViewBilling", targetRole: null, oldEffective: true, newEffective: false, reason: "Earlier reason", changedByUserId: "11111111-2222", viaDelegation: false, changedAtUtc: "2026-10-01T10:00:00Z" }], total: 1, page: 1, pageSize: 25 }));
    if (method === "PUT" && u === "/api/permissions/overrides") { s.version++; return Promise.resolve(json({ policyVersion: s.version, replayed: false, change: body.effect === "Revert" ? "Reverted" : body.effect === "Grant" ? "Granted" : "Revoked", role: body.role, permission: body.permission, effective: body.effect === "Grant", source: "granted", enabledUsers: 3 })); }
    return Promise.resolve(json({ error: "unexpected" }, 500));
  }));
});

afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

const openEditor = async (permission: string, role: string) => userEvent.click(await screen.findByRole("button", { name: `Change ${permission} for ${role}` }));
const put = () => s.calls.find((c) => c.method === "PUT" && c.url === "/api/permissions/overrides");

describe("ALV-N018 editable matrix", () => {
  it("shows the source of each cell, locks protected permissions and the fixed roles, and counts enabled accounts", async () => {
    render(<PermissionMatrixPage />);
    expect(await screen.findByRole("table", { name: /Permission matrix/ })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Change ManageUsers for Dentist" })).toBeNull();                 // protected: locked
    expect(screen.queryByRole("button", { name: "Change ViewBilling for Admin" })).toBeNull();                   // the administrator's cells are never editable
    expect(screen.queryByRole("button", { name: "Change ViewBilling for Unassigned" })).toBeNull();
    expect(screen.getByRole("button", { name: "Change ViewBilling for Dentist" })).toBeTruthy();
    expect(screen.getByText("3 enabled")).toBeTruthy();
    expect(screen.getAllByText("granted").length).toBeGreaterThan(0);                                            // the source tag of the Dentist ImportDocuments cell
    expect(screen.getByText(/locked: Administrator only/)).toBeTruthy();
  });

  it("needs a reason and a confirmation, sends the version seen and an idempotency key, and says it applies to the next request", async () => {
    render(<PermissionMatrixPage />);
    await openEditor("ViewBilling", "Dentist");
    expect(screen.getByText(/changes access for/)).toHaveTextContent("3");
    const save = screen.getByRole("button", { name: /Save: revoke/ });
    expect(save).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Reason/), "Dentists no longer see billing");
    expect(save).toBeDisabled();
    await userEvent.click(screen.getByRole("checkbox"));
    expect(save).toBeEnabled();
    await userEvent.click(save);
    expect(await screen.findByText(/for Dentist: revoked\. It applies to the next request/)).toBeTruthy();
    const call = put()!;
    expect(call.body).toMatchObject({ role: "Dentist", permission: "ViewBilling", effect: "Revoke", reason: "Dentists no longer see billing", expectedVersion: 7, confirmed: true });
    expect(call.headers["Idempotency-Key"]).toBeTruthy();
    await waitFor(() => expect(s.effectiveCalls).toBeGreaterThan(1));                                            // the matrix is re-read after a change
  });

  it("asks for the permission name typed exactly for a high-impact permission", async () => {
    render(<PermissionMatrixPage />);
    await openEditor("ViewPatientRecords", "Dentist");
    await userEvent.type(screen.getByLabelText(/Reason/), "Reason");
    await userEvent.click(screen.getByRole("checkbox"));
    const save = screen.getByRole("button", { name: /Save: revoke/ });
    expect(save).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Type ViewPatientRecords to confirm/), "viewpatientrecords");
    expect(save).toBeDisabled();
    await userEvent.clear(screen.getByLabelText(/Type ViewPatientRecords to confirm/));
    await userEvent.type(screen.getByLabelText(/Type ViewPatientRecords to confirm/), "ViewPatientRecords");
    expect(save).toBeEnabled();
    await userEvent.click(save);
    await waitFor(() => expect(put()?.body.typedConfirmation).toBe("ViewPatientRecords"));
  });

  it("offers a revert for a changed cell", async () => {
    render(<PermissionMatrixPage />);
    await openEditor("ImportDocuments", "Dentist");
    expect(screen.getByRole("heading", { name: /Revert ImportDocuments for Dentist/ })).toBeTruthy();
    await userEvent.type(screen.getByLabelText(/Reason/), "Back to default");
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: "Revert to default" }));
    await waitFor(() => expect(put()?.body.effect).toBe("Revert"));
  });

  it("explains a stale write, stores nothing and offers a reload", async () => {
    s.reply = ({ method, url }) => (method === "PUT" && url === "/api/permissions/overrides" ? json({ error: "policy_changed", message: "x" }, 409) : null);
    render(<PermissionMatrixPage />);
    await openEditor("ViewBilling", "Dentist");
    await userEvent.type(screen.getByLabelText(/Reason/), "Reason");
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: /Save: revoke/ }));
    expect((await screen.findByRole("alert")).textContent).toMatch(/policy changed since you opened this/);
    const before = s.effectiveCalls;
    await userEvent.click(screen.getByRole("button", { name: "Reload the policy" }));
    await waitFor(() => expect(s.effectiveCalls).toBeGreaterThan(before));
    expect(screen.queryByRole("heading", { name: /Revoke ViewBilling/ })).toBeNull();
  });

  it("keeps the same idempotency key for a retry of the same attempt and makes a new one when the form changes", async () => {
    let n = 0;
    s.reply = ({ method, url }) => (method === "PUT" && url === "/api/permissions/overrides" && n++ < 2 ? json({ error: "policy_write_failed", message: "x" }, 503) : null);
    render(<PermissionMatrixPage />);
    await openEditor("ViewBilling", "Dentist");
    await userEvent.type(screen.getByLabelText(/Reason/), "Reason");
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: /Save: revoke/ }));
    await screen.findByRole("alert");
    await userEvent.click(screen.getByRole("button", { name: /Save: revoke/ }));
    await waitFor(() => expect(s.calls.filter((c) => c.method === "PUT").length).toBe(2));
    const [first, second] = s.calls.filter((c) => c.method === "PUT");
    expect(first.headers["Idempotency-Key"]).toBe(second.headers["Idempotency-Key"]);
    await userEvent.type(screen.getByLabelText(/Reason/), " (edited)");
    await userEvent.click(screen.getByRole("button", { name: /Save: revoke/ }));
    await waitFor(() => expect(s.calls.filter((c) => c.method === "PUT").length).toBe(3));
    expect(s.calls.filter((c) => c.method === "PUT")[2].headers["Idempotency-Key"]).not.toBe(first.headers["Idempotency-Key"]);
  });

  it("re-reads the policy every half minute unless an editor is open, so a change elsewhere shows within about 30 seconds", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(<PermissionMatrixPage />);
    await screen.findByRole("table", { name: /Permission matrix/ });
    const first = s.effectiveCalls;
    await act(async () => { vi.advanceTimersByTime(MATRIX_REFRESH_MS + 100); });
    await waitFor(() => expect(s.effectiveCalls).toBeGreaterThan(first));
    await userEvent.click(screen.getByRole("button", { name: "Change ViewBilling for Dentist" }));
    const open = s.effectiveCalls;
    await act(async () => { vi.advanceTimersByTime(MATRIX_REFRESH_MS * 2); });
    expect(s.effectiveCalls).toBe(open);                                                                         // the form being edited is not pulled out from under the person
  });

  it("has no serious accessibility violations", async () => {
    const { container } = render(<PermissionMatrixPage />);
    await screen.findByRole("table", { name: /Permission matrix/ });
    await userEvent.click(screen.getByRole("button", { name: "Change ViewBilling for Dentist" }));
    expect((await axe(container)).violations).toEqual([]);
  });

  it("shows the read-only matrix, no change buttons and no history to a caller without the grant or audit permission", async () => {
    perms = new Set(["ViewPermissionMatrix"]);
    render(<PermissionMatrixPage />);
    expect(await screen.findByRole("columnheader", { name: "Dentist" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Change/ })).toBeNull();
    expect(screen.queryByRole("heading", { name: "Change history" })).toBeNull();
    expect(screen.queryByRole("heading", { name: "Policy health" })).toBeNull();
  });

  it("shows the change history to a holder of the audit permission", async () => {
    render(<PermissionMatrixPage />);
    const history = await screen.findByRole("heading", { name: "Change history" });
    expect(history).toBeTruthy();
    expect(await screen.findByText("Earlier reason")).toBeTruthy();
  });
});

describe("ALV-N018 health and repair", () => {
  it("says the policy is healthy, or lists each invalid row and removes one with a reason", async () => {
    const { unmount } = render(<PermissionMatrixPage />);
    expect(await screen.findByText(/policy is healthy/)).toBeTruthy();
    unmount();

    s.invalid = [{ id: "r1", table: "Override", role: "Dentist", permission: "ManageUsers", targetRole: null, effect: "Grant", reason: "ProtectedPermissionOverride", rowVersion: "AAAA" }];
    let removed = false;
    s.reply = ({ method, url }) => {
      if (method === "POST" && url === "/api/permissions/invalid-rows/r1/remove") { removed = true; s.invalid = []; s.version++; return json({ id: "r1", policyVersion: s.version }); }
      return null;
    };
    render(<PermissionMatrixPage />);
    expect((await screen.findByText(/Permission policy needs repair: 1 invalid row/)).textContent).toMatch(/Dentist/);
    await userEvent.click(screen.getByRole("button", { name: /Remove the invalid row for Dentist ManageUsers/ }));
    const submit = screen.getByRole("button", { name: "Remove the row" });
    expect(submit).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Reason/), "Corrupt row");
    await userEvent.click(submit);
    await waitFor(() => expect(removed).toBe(true));
    expect(s.calls.find((c) => c.url.includes("/remove"))?.body).toEqual({ reason: "Corrupt row", rowVersion: "AAAA" });
    expect(await screen.findByText(/policy is healthy/)).toBeTruthy();
  });
});

describe("ALV-N018 delegation screens", () => {
  it("lets an administrator remove and add delegation rules with a reason and a confirmation", async () => {
    render(<PermissionMatrixPage />);
    const panel = (await screen.findByRole("heading", { name: "Delegation rules" })).closest("section")!;
    await userEvent.click(within(panel).getByRole("button", { name: /Remove the rule: OfficeManager may change ImportDocuments for Dentist/ }));
    await userEvent.selectOptions(within(panel).getByLabelText("For role"), "Hygienist");
    await userEvent.click(within(panel).getByRole("button", { name: "Add the rule" }));
    await userEvent.type(within(panel).getByLabelText(/Reason/), "Hygienists instead of dentists");
    await userEvent.click(within(panel).getByRole("checkbox"));
    s.reply = ({ method, url }) => (method === "PUT" && url === "/api/permissions/delegations" ? json({ policyVersion: 8, replayed: false, change: "DelegationsChanged", role: null, permission: null, effective: null, source: null, enabledUsers: 0 }) : null);
    await userEvent.click(within(panel).getByRole("button", { name: "Save the rules" }));
    await waitFor(() => expect(s.calls.find((c) => c.method === "PUT" && c.url === "/api/permissions/delegations")).toBeTruthy());
    const call = s.calls.find((c) => c.method === "PUT" && c.url === "/api/permissions/delegations")!;
    expect(call.body).toMatchObject({ rows: [{ delegateRole: "OfficeManager", permission: "ImportDocuments", targetRole: "Hygienist" }], reason: "Hygienists instead of dentists", expectedVersion: 7, confirmed: true });
    expect(call.headers["Idempotency-Key"]).toBeTruthy();
  });

  it("shows the office manager the Who may import documents card and sends the change as a delegated grant", async () => {
    perms = new Set(["ViewPermissionMatrix", "ImportDocuments"]);
    s.my = [{ permission: "ImportDocuments", targets: [{ role: "Dentist", allowed: false, enabledUsers: 3 }, { role: "Billing", allowed: true, enabledUsers: 1 }] }];
    let granted: any = null;
    s.reply = ({ method, url, body }) => {
      if (method === "POST" && url === "/api/permissions/delegated-grants") { granted = body; s.my[0].targets[0].allowed = true; s.version++; return json({ policyVersion: s.version, replayed: false, change: "Granted", role: "Dentist", permission: "ImportDocuments", effective: true, source: "granted", enabledUsers: 3 }); }
      return null;
    };
    render(<PermissionMatrixPage />);
    expect(await screen.findByRole("heading", { name: "Who may import documents" })).toBeTruthy();
    await userEvent.click(screen.getByRole("button", { name: "Allow Dentist importing documents" }));
    expect(screen.getByText(/changes access for/)).toHaveTextContent("3");
    await userEvent.type(screen.getByLabelText(/Reason/), "Dentists scan their own radiographs");
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(granted).toMatchObject({ targetRole: "Dentist", permission: "ImportDocuments", allowed: true, reason: "Dentists scan their own radiographs", expectedVersion: 7, confirmed: true }));
    expect(await screen.findByText(/Dentist may now import documents/)).toBeTruthy();
  });

  it("shows a refusal of a delegated change in words and keeps the form", async () => {
    perms = new Set(["ViewPermissionMatrix"]);
    s.my = [{ permission: "ImportDocuments", targets: [{ role: "Dentist", allowed: false, enabledUsers: 3 }] }];
    s.reply = ({ method, url }) => (method === "POST" && url === "/api/permissions/delegated-grants" ? json({ error: "delegation_not_allowed", message: "no" }, 403) : null);
    render(<PermissionMatrixPage />);
    await userEvent.click(await screen.findByRole("button", { name: "Allow Dentist importing documents" }));
    await userEvent.type(screen.getByLabelText(/Reason/), "Reason");
    await userEvent.click(screen.getByRole("checkbox"));
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    expect((await screen.findByRole("alert")).textContent).toMatch(/may not change that permission for that role/);
    expect(screen.getByLabelText(/Reason/)).toHaveValue("Reason");
  });

  it("shows no delegation card to a role without a delegation", async () => {
    perms = new Set(["ViewPermissionMatrix"]);
    render(<PermissionMatrixPage />);
    await screen.findByRole("columnheader", { name: "Dentist" });
    expect(screen.queryByRole("heading", { name: "Who may import documents" })).toBeNull();
  });
});
