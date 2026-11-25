import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { StaffTab, OWNER_PROTECTED } from "./configuration/PeopleTabs";
import { AdminUsersPage } from "./AdminUsersPage";
import { RecoveryPage } from "./RecoveryPage";
import { PracticeOwnerNotice } from "./dashboard/PracticeOwnerNotice";
import { AuthProvider } from "../contexts/AuthContext";
import { NotificationProvider } from "../components/Notification";
import { signedInSession } from "../test/signedInSession";

/**
 * ALV-N030-C01 S38 (brief 18.8; review R10): the owner-administration screens. The server is the control for every rule; these tests prove what the screens
 * lock, show and send: the owner box locks the role to Administrator; the owner's Inactivate, Disable and role controls are locked with the transfer message;
 * only the Deployment Administrator sees "Transfer Practice Owner", whose dialog needs a reason, the password, an authenticator code when MFA is on and the
 * confirmation, sends the designation it showed, and keeps the input when the owner changed meanwhile; the recovery page; the degraded-owner notice.
 */

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());

type Call = { url: string; init?: RequestInit };
function stubFetch(handler: (url: string, init?: RequestInit) => Response | undefined) {
  const calls: Call[] = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((input: RequestInfo, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    return Promise.resolve(handler(url, init) ?? json([]));
  }));
  return calls;
}

const session = (fields: Record<string, unknown>) =>
  json(signedInSession({ username: "deploy", role: "Admin", permissions: ["ManageUsers", "ManagePracticeConfiguration", "ManageRoles", "ManageAccountStatus"], ...fields }));

const OWNER = { staffProfileId: "s-owner", displayName: "Dr Maya Owner", designatedAtUtc: "2026-11-25T00:00:00Z", method: "InitialByDeploymentAdministrator", designationId: "d-1" };
const user = (id: string, username: string, role: string, staff: string | null, extra: Record<string, unknown> = {}) => ({
  id, username, role, isDisabled: false, mfaEnabled: false, sessionTimeoutMinutes: 30, createdAtUtc: "2026-11-25T00:00:00Z",
  linkedStaffDisplayName: staff, linkedStaffIsActive: staff ? true : null, accountKind: "Staff", ...extra,
});
const USERS = [
  user("u-deploy", "deploy", "Admin", null, { accountKind: "DeploymentAdministrator" }),
  user("u-owner", "mowner", "Admin", "Dr Maya Owner"),
  user("u-next", "nadmin", "Admin", "Dr Next Admin"),
  user("u-desk", "sdesk", "FrontDesk", "Sam Desk"),
];
const STAFF = [
  { id: "s-owner", displayName: "Dr Maya Owner", jobTitle: null, userAccountId: "u-owner", isActive: true, rowVersion: "AA==" },
  { id: "s-next", displayName: "Dr Next Admin", jobTitle: null, userAccountId: "u-next", isActive: true, rowVersion: "AA==" },
  { id: "s-desk", displayName: "Sam Desk", jobTitle: null, userAccountId: "u-desk", isActive: true, rowVersion: "AA==" },
];

function wrap(ui: React.ReactElement) {
  return render(<AuthProvider><NotificationProvider><MemoryRouter>{ui}</MemoryRouter></NotificationProvider></AuthProvider>);
}

describe("the Staff tab", () => {
  it("locks the role to Administrator while the Practice Owner box is ticked, and explains why", async () => {
    stubFetch((url) => {
      if (url.includes("/api/auth/permissions")) return session({ practiceOwnerExists: false, accountKind: "DeploymentAdministrator" });
      if (url.includes("/api/config/staff")) return json([]);
    });
    wrap(<StaffTab onDirtyChange={() => {}} />);
    fireEvent.click(await screen.findByRole("button", { name: "Add staff member" }));
    const role = screen.getByLabelText("Role") as HTMLSelectElement;
    fireEvent.change(role, { target: { value: "Dentist" } });
    fireEvent.click(screen.getByLabelText("Make this person the Practice Owner"));
    expect(role.value).toBe("Admin");
    expect(role).toBeDisabled();
    expect(screen.getByText(/The Practice Owner signs in with the Administrator role/)).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("Make this person the Practice Owner"));
    expect(role).not.toBeDisabled();
  });

  it("disables Inactivate for the Practice Owner with the transfer message, and only for them", async () => {
    stubFetch((url) => {
      if (url.includes("/api/auth/permissions")) return session({ practiceOwnerExists: true, practiceOwnerStatus: "valid" });
      if (url.includes("/api/practice-owner")) return json({ owner: OWNER });
      if (url.includes("/api/config/staff")) return json(STAFF);
    });
    wrap(<StaffTab onDirtyChange={() => {}} />);
    await waitFor(() => expect(screen.getByRole("button", { name: "Inactivate Dr Maya Owner" })).toBeDisabled());
    expect(screen.getByText(OWNER_PROTECTED)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Inactivate Sam Desk" })).not.toBeDisabled();
  });
});

describe("Security Administration", () => {
  function renderUsers(fields: Record<string, unknown>, extra?: (url: string, init?: RequestInit) => Response | undefined) {
    const calls = stubFetch((url, init) => {
      if (url.includes("/api/auth/permissions")) return session(fields);
      const hit = extra?.(url, init);
      if (hit) return hit;
      if (url.includes("/api/practice-owner")) return json({ owner: OWNER });
      if (url.includes("/api/auth/users")) return json(USERS);
      if (url.includes("/api/config/staff")) return json(STAFF);
    });
    wrap(<AdminUsersPage />);
    return calls;
  }

  it("locks the owner's role and Disable with the transfer message", async () => {
    renderUsers({ accountKind: "Staff" });
    await waitFor(() => expect(screen.getByLabelText("Role for mowner")).toBeDisabled());
    const ownerRow = screen.getByText("mowner").closest("tr")!;
    expect(within(ownerRow).getByRole("button", { name: "Disable" })).toBeDisabled();
    expect(within(ownerRow).getByText(OWNER_PROTECTED)).toBeInTheDocument();
    expect(screen.getByLabelText("Role for nadmin")).not.toBeDisabled();
    // an ordinary Administrator never sees the transfer action
    expect(screen.queryByRole("button", { name: "Transfer Practice Owner" })).toBeNull();
  });

  it("lets the Deployment Administrator transfer with reason, password, code (MFA on) and confirmation, sending the designation it showed", async () => {
    const calls = renderUsers({ accountKind: "DeploymentAdministrator", username: "deploy" }, (url) => {
      if (url.includes("/api/auth/users")) return json(USERS.map((u) => (u.username === "deploy" ? { ...u, mfaEnabled: true } : u)));
      if (url.includes("/api/practice-owner/transfer"))
        return json({ designationId: "d-2", staffProfileId: "s-next", designatedAtUtc: "x", formerOwnerDisabled: false, replayed: false });
    });
    fireEvent.click(await screen.findByRole("button", { name: "Transfer Practice Owner" }));
    const dialog = await screen.findByRole("dialog", { name: "Transfer Practice Owner" });
    expect(await within(dialog).findByText("Dr Maya Owner")).toBeInTheDocument();
    const destination = within(dialog).getByLabelText("New Practice Owner") as HTMLSelectElement;
    // only enabled Administrator staff members other than the owner are offered
    expect(Array.from(destination.options).map((o) => o.textContent)).toEqual(["Select…", "Dr Next Admin"]);
    fireEvent.change(destination, { target: { value: "s-next" } });
    fireEvent.change(within(dialog).getByLabelText(/^Reason/), { target: { value: "The owner sold the practice." } });
    fireEvent.change(within(dialog).getByLabelText(/^Your password/), { target: { value: "Deploy-Password-1" } });
    fireEvent.change(within(dialog).getByLabelText(/^Authenticator code/), { target: { value: "123456" } });
    const transfer = within(dialog).getByRole("button", { name: "Transfer" });
    expect(transfer).toBeDisabled();   // not confirmed yet
    fireEvent.click(within(dialog).getByLabelText(/I am transferring the Practice Owner designation/));
    fireEvent.click(transfer);

    await waitFor(() => expect(calls.some((c) => c.url.includes("/api/practice-owner/transfer"))).toBe(true));
    const post = calls.find((c) => c.url.includes("/api/practice-owner/transfer"))!;
    expect(JSON.parse(String(post.init?.body))).toEqual({
      expectedDesignationId: "d-1", newOwnerStaffProfileId: "s-next", reason: "The owner sold the practice.", confirm: true,
      currentPassword: "Deploy-Password-1", mfaCode: "123456", disableFormerOwner: false,
    });
    expect((post.init?.headers as Record<string, string>)["Idempotency-Key"]).toBeTruthy();
    expect(await screen.findByText("Dr Next Admin is now the Practice Owner.")).toBeInTheDocument();
  });

  it("keeps what was typed and reloads the owner when the owner changed meanwhile", async () => {
    let ownerReads = 0;
    renderUsers({ accountKind: "DeploymentAdministrator", username: "deploy" }, (url) => {
      if (url.includes("/api/practice-owner/transfer")) return json({ error: "owner_changed", message: "changed" }, 409);
      if (url.endsWith("/api/practice-owner")) { ownerReads++; return json({ owner: OWNER }); }
    });
    fireEvent.click(await screen.findByRole("button", { name: "Transfer Practice Owner" }));
    const dialog = await screen.findByRole("dialog", { name: "Transfer Practice Owner" });
    await within(dialog).findByText("Dr Maya Owner");
    const readsBefore = ownerReads;
    fireEvent.change(within(dialog).getByLabelText("New Practice Owner"), { target: { value: "s-next" } });
    fireEvent.change(within(dialog).getByLabelText(/^Reason/), { target: { value: "The owner sold the practice." } });
    fireEvent.change(within(dialog).getByLabelText(/^Your password/), { target: { value: "Deploy-Password-1" } });
    expect(within(dialog).queryByLabelText(/^Authenticator code/)).toBeNull();   // MFA off: no code field
    fireEvent.click(within(dialog).getByLabelText(/I am transferring the Practice Owner designation/));
    fireEvent.click(within(dialog).getByRole("button", { name: "Transfer" }));

    expect(await within(dialog).findByText(/The Practice Owner changed since this dialog was opened/)).toBeInTheDocument();
    expect((within(dialog).getByLabelText(/^Reason/) as HTMLInputElement).value).toBe("The owner sold the practice.");
    expect((within(dialog).getByLabelText("New Practice Owner") as HTMLSelectElement).value).toBe("s-next");
    expect(within(dialog).getByLabelText(/I am transferring the Practice Owner designation/)).not.toBeChecked();   // confirm again
    await waitFor(() => expect(ownerReads).toBeGreaterThan(readsBefore));
  });
});

describe("the recovery page", () => {
  it("sends the token in the body and says what was done", async () => {
    const calls = stubFetch((url) => {
      if (url.includes("/api/recovery/deployment-administrator")) return json({ username: "deploy", method: "Reset" });
    });
    render(<MemoryRouter><RecoveryPage /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText("Recovery token"), { target: { value: " tok123 " } });
    fireEvent.change(screen.getByLabelText("Username"), { target: { value: "deploy" } });
    fireEvent.change(screen.getByLabelText(/^New password/), { target: { value: "Recovered-Password-2026" } });
    fireEvent.change(screen.getByLabelText("Confirm the new password"), { target: { value: "Recovered-Password-2026" } });
    fireEvent.click(screen.getByRole("button", { name: "Recover the Deployment Administrator" }));
    expect(await screen.findByText(/was reset: the new password is set, the account is enabled, and two-factor authentication is off/)).toBeInTheDocument();
    const post = calls.find((c) => c.url.includes("/api/recovery/deployment-administrator"))!;
    expect(post.url).not.toContain("tok123");
    expect(JSON.parse(String(post.init?.body))).toEqual({ token: "tok123", username: "deploy", newPassword: "Recovered-Password-2026" });
  });

  it("gives the same message for any token or username failure", async () => {
    stubFetch((url) => {
      if (url.includes("/api/recovery/deployment-administrator")) return json({ error: "recovery_failed" }, 400);
    });
    render(<MemoryRouter><RecoveryPage /></MemoryRouter>);
    fireEvent.change(screen.getByLabelText("Recovery token"), { target: { value: "wrong" } });
    fireEvent.change(screen.getByLabelText("Username"), { target: { value: "deploy" } });
    fireEvent.change(screen.getByLabelText(/^New password/), { target: { value: "Recovered-Password-2026" } });
    fireEvent.change(screen.getByLabelText("Confirm the new password"), { target: { value: "Recovered-Password-2026" } });
    fireEvent.click(screen.getByRole("button", { name: "Recover the Deployment Administrator" }));
    expect(await screen.findByText(/Recovery failed\. The token may be wrong, expired or already used/)).toBeInTheDocument();
  });
});

describe("the degraded-owner notice", () => {
  it("names the failing conditions and the fixes to an administrator", async () => {
    stubFetch((url) => {
      if (url.includes("/api/auth/permissions"))
        return session({ practiceOwnerExists: true, practiceOwnerStatus: "degraded", practiceOwnerProblems: ["notAdministrator", "loginDisabled"] });
    });
    wrap(<PracticeOwnerNotice />);
    expect(await screen.findByText(/their login is not an Administrator; their login is disabled/)).toBeInTheDocument();
    expect(screen.getByText(/have the Deployment Administrator transfer the Practice Owner designation/)).toBeInTheDocument();
  });
});
