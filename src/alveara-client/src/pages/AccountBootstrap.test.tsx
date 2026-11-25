import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { SetupPage } from "./SetupPage";
import { ChangePasswordPage } from "./ChangePasswordPage";
import { PracticeOwnerNotice, DEPLOYMENT_ADMINISTRATOR_NOTICE } from "./dashboard/PracticeOwnerNotice";
import { TemporaryPasswordPanel } from "./configuration/StaffMemberDialogs";
import { StaffTab } from "./configuration/PeopleTabs";
import { accountLabels } from "./accountLabels";
import { RequireAuth } from "../app/RouteGuards";
import { StartupGate } from "../app/StartupGate";
import { AuthProvider } from "../contexts/AuthContext";
import { NotificationProvider } from "../components/Notification";
import { signedInSession } from "../test/signedInSession";
import type { AdminUserSummary } from "../services/authApi";

/**
 * ALV-N030 slice 6 (brief section 9): first-run setup, the forced password change, "Add staff member" with the one-time temporary password, the
 * legacy-login action, the Practice Owner notices and the Security Administration labels. The server is the authority for every rule; these tests
 * prove what the screens send and show.
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

function session(fields: Record<string, unknown>) {
  return json(signedInSession({ username: "someone", role: "Admin", permissions: ["ManageUsers", "ManagePracticeConfiguration"], ...fields }));
}

function Where() {
  return <span data-testid="where">{useLocation().pathname + useLocation().search}</span>;
}

describe("first-run setup page", () => {
  it("sends the token in the JSON body only, waits while the server restarts, then goes to sign-in", async () => {
    let restarted = false;
    let downChecks = 0;
    const calls = stubFetch((url) => {
      if (url.includes("/api/setup/status")) {
        if (!restarted) return json({ mode: "setup", setupAvailable: true });
        if (downChecks++ < 2) throw new TypeError("Failed to fetch");                     // the server is down while it restarts
        return json({ mode: "normal", setupAvailable: false });
      }
      if (url.includes("/api/setup/deployment-administrator")) { restarted = true; return json({ id: "1", username: "deploy" }, 201); }
    });
    render(<MemoryRouter initialEntries={["/setup"]}><Routes><Route path="/setup" element={<SetupPage pollMs={10} />} /><Route path="/login" element={<Where />} /></Routes></MemoryRouter>);
    fireEvent.change(await screen.findByLabelText("Setup token"), { target: { value: "  abc123  " } });
    fireEvent.change(screen.getByLabelText("Username"), { target: { value: "deploy" } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: "a-long-enough-pass" } });
    fireEvent.change(screen.getByLabelText("Confirm the password"), { target: { value: "a-long-enough-pass" } });
    fireEvent.click(screen.getByRole("button", { name: "Create the Deployment Administrator" }));
    expect(await screen.findByText("Setup is complete. DentalKeep is restarting…")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId("where").textContent).toBe("/login?setup=complete"));
    expect(downChecks).toBeGreaterThanOrEqual(2);
    const post = calls.find((c) => c.url.includes("/api/setup/deployment-administrator"))!;
    expect(post.url).not.toContain("abc123");
    expect(JSON.parse(String(post.init?.body))).toEqual({ token: "abc123", username: "deploy", password: "a-long-enough-pass" });
  });

  it("shows one message for every token failure and checks the password before sending", async () => {
    const calls = stubFetch((url) => {
      if (url.includes("/api/setup/status")) return json({ setupAvailable: true });
      if (url.includes("/api/setup/deployment-administrator")) return json({ error: "setup_failed" }, 400);
    });
    render(<MemoryRouter><SetupPage /></MemoryRouter>);
    fireEvent.change(await screen.findByLabelText("Setup token"), { target: { value: "wrong" } });
    fireEvent.change(screen.getByLabelText("Username"), { target: { value: "deploy" } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: "short" } });
    fireEvent.change(screen.getByLabelText("Confirm the password"), { target: { value: "short" } });
    fireEvent.click(screen.getByRole("button", { name: "Create the Deployment Administrator" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("at least 12 characters");
    expect(calls.some((c) => c.url.includes("/api/setup/deployment-administrator"))).toBe(false);

    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: "a-long-enough-pass" } });
    fireEvent.change(screen.getByLabelText("Confirm the password"), { target: { value: "a-long-enough-pass" } });
    fireEvent.click(screen.getByRole("button", { name: "Create the Deployment Administrator" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("The setup token is not valid"));
  });

  it("says the server did not come back when it never returns in time", async () => {
    let done = false;
    stubFetch((url) => {
      if (url.includes("/api/setup/status")) { if (done) throw new TypeError("Failed to fetch"); return json({ mode: "setup", setupAvailable: true }); }
      if (url.includes("/api/setup/deployment-administrator")) { done = true; return json({ id: "1", username: "deploy" }, 201); }
    });
    render(<MemoryRouter><SetupPage pollMs={5} giveUpMs={60} /></MemoryRouter>);
    fireEvent.change(await screen.findByLabelText("Setup token"), { target: { value: "t" } });
    fireEvent.change(screen.getByLabelText("Username"), { target: { value: "deploy" } });
    fireEvent.change(screen.getByLabelText(/^Password/), { target: { value: "a-long-enough-pass" } });
    fireEvent.change(screen.getByLabelText("Confirm the password"), { target: { value: "a-long-enough-pass" } });
    fireEvent.click(screen.getByRole("button", { name: "Create the Deployment Administrator" }));
    expect(await screen.findByText("DentalKeep did not come back. Check that the service is running.")).toBeInTheDocument();
  });

  it("says so once the installation is set up, with no form", async () => {
    stubFetch((url) => (url.includes("/api/setup/status") ? json({ setupAvailable: false }) : undefined));
    render(<MemoryRouter><SetupPage /></MemoryRouter>);
    expect(await screen.findByText("This installation has already been set up.")).toBeInTheDocument();
    expect(screen.queryByLabelText("Setup token")).toBeNull();
    expect(screen.getByRole("link", { name: "Go to sign in" })).toBeInTheDocument();
  });
});

describe("the forced password change", () => {
  it("sends a signed-in account with a temporary password to Choose a new password, and back to the app after the change", async () => {
    let forced = true;
    const calls = stubFetch((url) => {
      if (url.includes("/api/auth/permissions")) return session({ mustChangePassword: forced });
      if (url.includes("/api/auth/password/change")) { forced = false; return new Response(null, { status: 200 }); }
    });
    render(
      <AuthProvider>
        <MemoryRouter initialEntries={["/"]}>
          <Routes>
            <Route path="/change-password" element={<ChangePasswordPage />} />
            <Route path="/" element={<RequireAuth><span>the app</span></RequireAuth>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    );
    expect(await screen.findByRole("heading", { name: "Choose a new password" })).toBeInTheDocument();
    expect(screen.queryByText("the app")).toBeNull();
    fireEvent.change(screen.getByLabelText("Temporary password"), { target: { value: "Temp-Pass-123456" } });
    fireEvent.change(screen.getByLabelText(/^New password/), { target: { value: "My-Own-Password-1" } });
    fireEvent.change(screen.getByLabelText("Confirm the new password"), { target: { value: "My-Own-Password-1" } });
    fireEvent.click(screen.getByRole("button", { name: "Save my new password" }));
    expect(await screen.findByText("the app")).toBeInTheDocument();
    const change = calls.find((c) => c.url.includes("/api/auth/password/change"))!;
    expect(JSON.parse(String(change.init?.body))).toEqual({ currentPassword: "Temp-Pass-123456", newPassword: "My-Own-Password-1" });
  });

  it("refuses a mismatch on screen and maps a wrong current password", async () => {
    stubFetch((url) => {
      if (url.includes("/api/auth/permissions")) return session({ mustChangePassword: true });
      if (url.includes("/api/auth/password/change")) return json({ error: "invalid_current_password" }, 400);
    });
    render(<AuthProvider><MemoryRouter><ChangePasswordPage /></MemoryRouter></AuthProvider>);
    fireEvent.change(await screen.findByLabelText("Temporary password"), { target: { value: "wrong" } });
    fireEvent.change(screen.getByLabelText(/^New password/), { target: { value: "My-Own-Password-1" } });
    fireEvent.change(screen.getByLabelText("Confirm the new password"), { target: { value: "Different-Pass-2" } });
    fireEvent.click(screen.getByRole("button", { name: "Save my new password" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("do not match");
    fireEvent.change(screen.getByLabelText("Confirm the new password"), { target: { value: "My-Own-Password-1" } });
    fireEvent.click(screen.getByRole("button", { name: "Save my new password" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("The current password is not correct."));
  });
});

describe("the temporary password panel", () => {
  it("shows the password once with the instruction, and Done hides it", () => {
    const onDismiss = vi.fn();
    render(<TemporaryPasswordPanel issued={{ displayName: "Pat Lee", username: "pat.lee", temporaryPassword: "Abc23456Defg7890" }} onDismiss={onDismiss} />);
    expect(screen.getByTestId("temporary-password").textContent).toBe("Abc23456Defg7890");
    expect(screen.getByText("Give this password to the person; they must change it when they first sign in.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Done, hide the password" }));
    expect(onDismiss).toHaveBeenCalled();
  });
});

describe("Add staff member on the Staff tab", () => {
  function renderStaffTab(fields: Record<string, unknown>, rows: unknown[], extra?: (url: string, init?: RequestInit) => Response | undefined) {
    const calls = stubFetch((url, init) => {
      if (url.includes("/api/auth/permissions")) return session(fields);
      const hit = extra?.(url, init);
      if (hit) return hit;
      if (url.includes("/api/config/staff")) return json(rows);
    });
    render(<AuthProvider><NotificationProvider><MemoryRouter><StaffTab onDirtyChange={() => {}} /></MemoryRouter></NotificationProvider></AuthProvider>);
    return calls;
  }

  it("adds profile, login and the owner designation in ONE call with an Idempotency-Key, and shows the temporary password", async () => {
    const calls = renderStaffTab({ practiceOwnerExists: false, accountKind: "DeploymentAdministrator" }, [], (url) => {
      if (url.includes("/api/config/staff-members"))
        return json({ staffProfileId: "s1", userAccountId: "u1", username: "dr.owner", role: "Admin", practiceOwner: true, temporaryPassword: "Owner2345Pass6789" }, 201);
    });
    fireEvent.click(await screen.findByRole("button", { name: "Add staff member" }));
    expect(screen.queryByLabelText(/Login account/)).toBeNull();                       // no link picker
    fireEvent.change(screen.getByLabelText("Display name"), { target: { value: "Dr Owner" } });
    fireEvent.change(screen.getByLabelText(/^Username/), { target: { value: "dr.owner" } });
    fireEvent.change(screen.getByLabelText("Role"), { target: { value: "Admin" } });
    fireEvent.click(screen.getByLabelText("Make this person the Practice Owner"));
    const form = screen.getByRole("form", { name: "Add staff member" });
    fireEvent.click(within(form).getByRole("button", { name: "Add staff member" }));

    expect(await screen.findByTestId("temporary-password")).toHaveTextContent("Owner2345Pass6789");
    expect(await screen.findByText("Dr Owner is now the Practice Owner.")).toBeInTheDocument();
    const add = calls.find((c) => c.url.includes("/api/config/staff-members"))!;
    expect((add.init?.headers as Record<string, string>)["Idempotency-Key"]).toBe("staff-member:dr.owner");
    expect(JSON.parse(String(add.init?.body))).toEqual({ displayName: "Dr Owner", jobTitle: "", username: "dr.owner", role: "Admin", practiceOwner: true });
    // ALV-N030-C01 (brief 18.4): the server designates in the same transaction - the client never makes a second, separate designation call
    expect(calls.some((c) => c.url.includes("/api/practice-owner/initial"))).toBe(false);
    expect(calls.some((c) => c.url.endsWith("/api/config/staff") && c.init?.method === "POST")).toBe(false);   // never the removed profile-only create
  });

  it("shows the server's refusal when an owner was designated meanwhile, and makes no second call", async () => {
    const calls = renderStaffTab({ practiceOwnerExists: false, accountKind: "DeploymentAdministrator" }, [], (url) => {
      if (url.includes("/api/config/staff-members"))
        return json({ error: "owner_already_designated", message: "A Practice Owner has already been designated." }, 409);
    });
    fireEvent.click(await screen.findByRole("button", { name: "Add staff member" }));
    fireEvent.change(screen.getByLabelText("Display name"), { target: { value: "Dr Late" } });
    fireEvent.change(screen.getByLabelText(/^Username/), { target: { value: "dr.late" } });
    fireEvent.change(screen.getByLabelText("Role"), { target: { value: "Admin" } });
    fireEvent.click(screen.getByLabelText("Make this person the Practice Owner"));
    fireEvent.click(within(screen.getByRole("form", { name: "Add staff member" })).getByRole("button", { name: "Add staff member" }));
    expect(await screen.findByText(/A Practice Owner has already been designated\. Nobody was added/)).toBeInTheDocument();
    expect(screen.queryByTestId("temporary-password")).toBeNull();
    expect(calls.filter((c) => c.url.includes("/api/config/staff-members"))).toHaveLength(1);
    expect(calls.some((c) => c.url.includes("/api/practice-owner/initial"))).toBe(false);
  });

  it("offers no owner box once an owner exists, and says why adding needs Manage users", async () => {
    stubFetch((url) => {
      if (url.includes("/api/auth/permissions")) return json(signedInSession({ username: "om", role: "OfficeManager", permissions: ["ManagePracticeConfiguration"], practiceOwnerExists: true }));
      if (url.includes("/api/config/staff")) return json([]);
    });
    render(<AuthProvider><NotificationProvider><MemoryRouter><StaffTab onDirtyChange={() => {}} /></MemoryRouter></NotificationProvider></AuthProvider>);
    expect(await screen.findByText("Adding a staff member also creates their login, which needs the Manage users permission.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add staff member" })).toBeDisabled();
  });

  it("labels a record without a login as legacy and creates a login for it", async () => {
    const legacy = { id: "s9", displayName: "Legacy Lou", jobTitle: null, userAccountId: null, isActive: true, credentialLabel: null, licenseNumber: null, licenseJurisdiction: null, npi: null, rowVersion: "AA==" };
    const calls = renderStaffTab({ practiceOwnerExists: true }, [legacy], (url) => {
      if (url.includes("/api/config/staff/s9/legacy-login"))
        return json({ staffProfileId: "s9", userAccountId: "u9", username: "lou", role: "Hygienist", temporaryPassword: "Lou23456Pass7890" }, 201);
    });
    expect(await screen.findByText("Legacy record without a login")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Create a login for this legacy staff record/ }));
    const group = screen.getByRole("group", { name: "Create a login for Legacy Lou" });
    fireEvent.change(within(group).getByLabelText("Username"), { target: { value: "lou" } });
    fireEvent.change(within(group).getByLabelText("Role"), { target: { value: "Hygienist" } });
    fireEvent.click(within(group).getByRole("button", { name: "Create login" }));
    expect(await screen.findByTestId("temporary-password")).toHaveTextContent("Lou23456Pass7890");
    const post = calls.find((c) => c.url.includes("/legacy-login"))!;
    expect((post.init?.headers as Record<string, string>)["Idempotency-Key"]).toBe("legacy-login:s9:lou");
  });
});

describe("the Practice Owner notices on the dashboard", () => {
  it("tells the Deployment Administrator to add the owner as a staff member", async () => {
    stubFetch((url) => (url.includes("/api/auth/permissions") ? session({ accountKind: "DeploymentAdministrator", practiceOwnerExists: false }) : undefined));
    render(<AuthProvider><MemoryRouter><PracticeOwnerNotice /></MemoryRouter></AuthProvider>);
    expect(await screen.findByText(DEPLOYMENT_ADMINISTRATOR_NOTICE)).toBeInTheDocument();
  });

  it("lets an administrator of an upgraded practice designate an eligible staff member after confirming", async () => {
    const rows = [
      { id: "a", displayName: "Dr Able", jobTitle: "Dentist", userAccountId: "ua", isActive: true, rowVersion: "AA==" },
      { id: "b", displayName: "No Login", jobTitle: null, userAccountId: null, isActive: true, rowVersion: "AA==" },
    ];
    const calls = stubFetch((url) => {
      if (url.includes("/api/auth/permissions")) return session({ accountKind: "Staff", practiceOwnerExists: false });
      if (url.includes("/api/practice-owner/initial")) return json({ staffProfileId: "a", displayName: "Dr Able", designatedAtUtc: "x", method: "UpgradeTransition" }, 201);
      if (url.includes("/api/config/staff")) return json(rows);
    });
    render(<AuthProvider><MemoryRouter><PracticeOwnerNotice /></MemoryRouter></AuthProvider>);
    fireEvent.click(await screen.findByRole("button", { name: "Designate the Practice Owner" }));
    const select = await screen.findByRole("combobox", { name: "Practice Owner" });
    expect(within(select).queryByRole("option", { name: "No Login" })).toBeNull();   // only staff with a login are eligible
    fireEvent.change(select, { target: { value: "a" } });
    expect(screen.getByRole("button", { name: "Designate" })).toBeDisabled();       // the confirmation step comes first
    fireEvent.click(screen.getByLabelText(/I confirm this person is the Practice Owner/));
    fireEvent.click(screen.getByRole("button", { name: "Designate" }));
    await waitFor(() => expect(calls.some((c) => c.url.includes("/api/practice-owner/initial"))).toBe(true));
    const post = calls.find((c) => c.url.includes("/api/practice-owner/initial"))!;
    expect(JSON.parse(String(post.init?.body))).toEqual({ staffProfileId: "a", confirm: true });
  });

  it("shows nothing once an owner exists", async () => {
    stubFetch((url) => (url.includes("/api/auth/permissions") ? session({ accountKind: "DeploymentAdministrator", practiceOwnerExists: true }) : undefined));
    const { container } = render(<AuthProvider><MemoryRouter><PracticeOwnerNotice /><span>loaded</span></MemoryRouter></AuthProvider>);
    await screen.findByText("loaded");
    await waitFor(() => expect(container.querySelector(".alv-dashboard-notice")).toBeNull());
  });
});

describe("Security Administration labels", () => {
  const base: AdminUserSummary = { id: "1", username: "u", role: "Admin", isDisabled: false, mfaEnabled: false, sessionTimeoutMinutes: 30, createdAtUtc: "2026-01-01T00:00:00Z" };
  it.each([
    [{ accountKind: "DeploymentAdministrator" }, null, ["Deployment Administrator"]],
    [{ role: "Unassigned" }, null, ["Legacy account (unassigned)"]],
    [{ linkedStaffDisplayName: null }, null, ["Legacy account without a staff profile"]],
    [{ linkedStaffDisplayName: "Dr Owner" }, "Dr Owner", ["Practice Owner"]],
    [{ linkedStaffDisplayName: "Pat", mustChangePassword: true }, null, ["Must change password"]],
  ])("%j -> %j", (fields, owner, expected) => {
    expect(accountLabels({ ...base, ...fields } as AdminUserSummary, owner)).toEqual(expected);
  });
});

describe("the start-up check (brief 16.2a)", () => {
  function renderGate(initial = "/") {
    render(
      <MemoryRouter initialEntries={[initial]}>
        <StartupGate>
          <Routes>
            <Route path="/setup" element={<span>setup page</span>} />
            <Route path="*" element={<span>the app</span>} />
          </Routes>
        </StartupGate>
      </MemoryRouter>
    );
  }

  it.each([
    ["installation_incomplete", "DentalKeep is not installed yet. Run the installer."],
    ["upgrade_required", "An upgrade is waiting. An administrator must run the upgrade tool."],
  ])("a server in %s shows only its message", async (mode, text) => {
    stubFetch((url) => (url.includes("/api/setup/status") ? json({ mode }, 503) : undefined));
    renderGate();
    expect(await screen.findByText(text)).toBeInTheDocument();
    expect(screen.queryByText("the app")).toBeNull();
  });

  it("a never-set-up server opens first-run setup", async () => {
    stubFetch((url) => (url.includes("/api/setup/status") ? json({ mode: "setup", setupAvailable: true }) : undefined));
    renderGate("/login");
    expect(await screen.findByText("setup page")).toBeInTheDocument();
  });

  it("after setup and the restart, leaving /setup asks again instead of returning to setup", async () => {
    let restarted = false;
    stubFetch((url) => (url.includes("/api/setup/status") ? json(restarted ? { mode: "normal", setupAvailable: false } : { mode: "setup", setupAvailable: true }) : undefined));
    function Leave() { return <button onClick={() => { restarted = true; navigateTo("/login"); }}>leave</button>; }
    let navigateTo: (to: string) => void = () => {};
    function Capture() { const n = useNavigate(); navigateTo = n; return null; }
    render(
      <MemoryRouter initialEntries={["/setup"]}>
        <StartupGate>
          <Capture />
          <Routes>
            <Route path="/setup" element={<Leave />} />
            <Route path="/login" element={<span>sign in</span>} />
          </Routes>
        </StartupGate>
      </MemoryRouter>
    );
    fireEvent.click(await screen.findByRole("button", { name: "leave" }));
    expect(await screen.findByText("sign in")).toBeInTheDocument();
  });

  it("a set-up server, or one that cannot be asked, shows the app as before", async () => {
    stubFetch((url) => (url.includes("/api/setup/status") ? json({ mode: "normal", setupAvailable: false }) : undefined));
    renderGate();
    expect(await screen.findByText("the app")).toBeInTheDocument();
    vi.unstubAllGlobals();
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
    renderGate();
    expect((await screen.findAllByText("the app")).length).toBeGreaterThan(0);
  });
});
