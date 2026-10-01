import { describe, it, expect, vi, afterEach } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { MemoryRouter } from "react-router-dom";
import { BackupRecoveryPage } from "./BackupRecoveryPage";
import { AuthProvider } from "../contexts/AuthContext";
import { NotificationProvider } from "../components/Notification";
import type { ArchiveInfo, BackupRecord, BackupSettings, BackupStatus, RestoreDrill } from "../services/backupApi";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const settings = (over: Partial<BackupSettings> = {}): BackupSettings => ({
  scheduleEnabled: true, scheduleIntervalHours: 24, retentionCount: 7, destinationDirectory: "D:\\Backups", destinationIsDefault: false,
  recoveryKeyConfigured: true, recoveryKeyFingerprint: "abcdef0123456789abcdef", recoveryKeyConfiguredAtUtc: "2026-09-01T10:00:00Z",
  requiredSuccessfulVerifications: 2, verificationCadenceDays: 30, rowVersion: "AAAA", ...over,
});

const record = (over: Partial<BackupRecord> = {}): BackupRecord => ({
  id: "b1", kind: "Manual", status: "Succeeded", startedAtUtc: "2026-09-30T08:00:00Z", completedAtUtc: "2026-09-30T08:01:00Z", fileName: "alveara-backup-1.abk",
  sizeBytes: 5_242_880, sha256: "ff", includedAssetClasses: ["database", "documents", "dataProtectionKeys"], schemaMigration: "2026", appVersion: "1",
  failureCode: null, failureMessage: null, verificationStatus: "HashVerified", verifiedAtUtc: "2026-09-30T08:01:00Z", verificationFailureCode: null, restoreProven: false, archiveDefectCode: null, ...over,
});

const status = (over: Partial<BackupStatus> = {}): BackupStatus => ({
  settings: settings(), lastSuccess: record(), lastFailure: null, latestAttemptFailed: false, successfulVerificationCount: 2, trusted: true,
  lastFullVerificationAtUtc: "2026-09-29T08:00:00Z", verificationOverdue: false, requiredAssetClasses: ["database", "documents", "dataProtectionKeys"],
  lastSuccessCoversAllAssetClasses: true, missingAssetClasses: [], ...over,
});

interface Api {
  status: BackupStatus;
  history: BackupRecord[];
  drills: RestoreDrill[];
  archives: ArchiveInfo[];
  notifications: unknown[];
  permissions: string[];
  handlers: Record<string, (init?: RequestInit) => Response | Promise<Response>>;
  calls: { url: string; method: string; body: Record<string, unknown> | null }[];
}

function stubApi(over: Partial<Api> = {}): Api {
  const api: Api = { status: status(), history: [record()], drills: [], archives: [], notifications: [], permissions: ["ViewBackupStatus", "ManageBackups"], handlers: {}, calls: [], ...over };
  vi.stubGlobal(
    "fetch",
    vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
      const u = String(url);
      const method = init?.method ?? "GET";
      api.calls.push({ url: u, method, body: init?.body ? JSON.parse(init.body as string) : null });
      for (const [fragment, handler] of Object.entries(api.handlers)) if (u.includes(fragment)) return Promise.resolve(handler(init));
      if (u.includes("/api/auth/permissions"))
        return Promise.resolve(jsonResponse({ username: "admin", role: "Admin", permissions: api.permissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }));
      if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "csrf" }));
      if (u.includes("/api/backup/status")) return Promise.resolve(jsonResponse(api.status));
      if (u.includes("/api/backup/history")) return Promise.resolve(jsonResponse(api.history));
      if (u.includes("/api/backup/restore-drills")) return Promise.resolve(jsonResponse(api.drills));
      if (u.includes("/api/backup/archives") && method === "GET") return Promise.resolve(jsonResponse(api.archives));
      if (u.includes("/api/backup/notifications")) return Promise.resolve(jsonResponse(api.notifications));
      return Promise.resolve(jsonResponse({}));
    })
  );
  return api;
}

function renderPage() {
  return render(
    <MemoryRouter>
      <NotificationProvider>
        <AuthProvider>
          <BackupRecoveryPage />
        </AuthProvider>
      </NotificationProvider>
    </MemoryRouter>
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

const drill = (over: Partial<RestoreDrill> = {}): RestoreDrill => ({
  id: "d1", backupRecordId: "b1", sourceKind: "History", archiveFileName: null, archiveSha256: null, startedAtUtc: "2026-09-30T09:00:00Z", completedAtUtc: "2026-09-30T09:02:00Z", outcome: "Succeeded", failureCode: null, failureMessage: null,
  targetDatabase: "AlveraRestore_abc", targetDirectory: "C:\\restore\\abc", targetRemoved: false,
  checks: [{ name: "documents_complete", passed: true, detail: "Every document/blob listed in the manifest is present.", blocking: true }], ...over,
});

describe("Backup & recovery page - truthful status", () => {
  it("shows the last success, schedule, storage target, verification state and asset coverage", async () => {
    stubApi();
    renderPage();
    expect(await screen.findByText(/Last successful backup/)).toBeInTheDocument();
    expect(screen.getByText(/Every 24 hour\(s\), keeping the latest 7/)).toBeInTheDocument();
    expect(screen.getByText(/D:\\Backups/)).toBeInTheDocument();
    expect(screen.getByText(/2 of 2 required restore drill/)).toBeInTheDocument();
    expect(screen.getByLabelText("Asset coverage")).toHaveTextContent(/includes every managed asset class: Database, Documents, Encryption keys/);
    expect(screen.queryByText("The most recent backup FAILED")).not.toBeInTheDocument();
  });

  it("makes a failed latest attempt the headline - not hidden behind the older success", async () => {
    stubApi({
      status: status({
        latestAttemptFailed: true,
        lastFailure: record({ id: "b2", status: "Failed", failureCode: "destination_full", sizeBytes: null, completedAtUtc: "2026-10-01T02:00:00Z" }),
      }),
    });
    renderPage();
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("The most recent backup FAILED");
    expect(alert).toHaveTextContent("The backup destination is out of space.");
    expect(alert).toHaveTextContent(/The last successful backup was/);
  });

  it("says plainly that scheduled backups are on probation until enough verifications pass", async () => {
    stubApi({ status: status({ trusted: false, successfulVerificationCount: 0 }) });
    renderPage();
    expect(await screen.findByText("Scheduled backups are on probation")).toBeInTheDocument();
    expect(screen.getByText(/only 0 of 2 required restore drill/)).toBeInTheDocument();
  });

  it("flags an overdue full verification even when the file hash passed", async () => {
    stubApi({ status: status({ verificationOverdue: true, lastFullVerificationAtUtc: null }) });
    renderPage();
    expect(await screen.findByText("A restore drill is overdue")).toBeInTheDocument();
    expect(screen.getByText(/not proven restorable/)).toBeInTheDocument();
  });

  it("warns when the last backup does not cover every managed asset class", async () => {
    stubApi({ status: status({ lastSuccessCoversAllAssetClasses: false, missingAssetClasses: ["dataProtectionKeys"] }) });
    renderPage();
    expect(await screen.findByLabelText("Asset coverage")).toHaveTextContent(/does NOT include every managed asset class. Missing: Encryption keys/);
  });

  it("tells the administrator that nothing can be backed up before a recovery key exists", async () => {
    stubApi({ status: status({ settings: settings({ recoveryKeyConfigured: false, scheduleEnabled: false, recoveryKeyFingerprint: null }), lastSuccess: null }), history: [] });
    renderPage();
    expect(await screen.findByText("No recovery key is set up")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Run backup now" })).toBeDisabled();
    expect(screen.getByText("No backups yet")).toBeInTheDocument();
  });

  it("history keeps success, failure and verification as separate truths", async () => {
    stubApi({
      history: [
        record({ id: "b1", verificationStatus: "FullyVerified", restoreProven: true }),
        record({ id: "b2", startedAtUtc: "2026-09-29T08:00:00Z", verificationStatus: "HashVerified" }),
        record({ id: "b3", startedAtUtc: "2026-09-28T08:00:00Z", status: "Failed", failureCode: "destination_unavailable", includedAssetClasses: [], sizeBytes: null, verificationStatus: "NotVerified" }),
        record({ id: "b4", startedAtUtc: "2026-09-27T08:00:00Z", verificationStatus: "VerificationFailed", verificationFailureCode: "hash_mismatch" }),
      ],
    });
    renderPage();
    const table = await screen.findByRole("table");
    expect(within(table).getByText("Fully verified (restorable)")).toBeInTheDocument();
    expect(within(table).getByText("File hash verified only")).toBeInTheDocument();
    expect(within(table).getByText("Verification FAILED")).toBeInTheDocument();
    expect(within(table).getByText(/does not match the hash recorded/)).toBeInTheDocument();
    expect(within(table).getByText("Failed")).toBeInTheDocument();
    expect(within(table).getByText(/destination is not available or not writable/)).toBeInTheDocument();
  });
});

describe("Backup & recovery page - authorization", () => {
  it("shows a clear permission-denied state, not a blank page, when the server refuses the status", async () => {
    stubApi({ handlers: { "/api/backup/status": () => jsonResponse({ error: "permission_denied", required: "ViewBackupStatus" }, 403) } });
    renderPage();
    expect(await screen.findByText(/don't have permission/i)).toBeInTheDocument();
  });

  it("a view-only role sees status, history and failures but none of the management actions", async () => {
    stubApi({ permissions: ["ViewBackupStatus"] });
    renderPage();
    expect(await screen.findByText(/Last successful backup/)).toBeInTheDocument();
    expect(screen.getByRole("table")).toBeInTheDocument();
    for (const name of ["Run backup now", "Send test notification", "Set up recovery key", "Replace recovery key", "Save settings"])
      expect(screen.queryByRole("button", { name })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Verify or restore/ })).not.toBeInTheDocument();
    expect(screen.queryByText("Recovery key")).not.toBeInTheDocument();
  });
});

describe("Backup & recovery page - manual backup and notifications", () => {
  it("runs a backup, reports success and refreshes the history", async () => {
    const api = stubApi({ handlers: { "/api/backup/backups": (init) => (init?.method === "POST" ? jsonResponse(record({ id: "b9" }), 201) : jsonResponse([])) } });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Run backup now" }));
    expect(await screen.findByText("Backup completed.")).toBeInTheDocument();
    await waitFor(() => expect(api.calls.filter((c) => c.url.includes("/api/backup/history")).length).toBeGreaterThanOrEqual(2));
  });

  it("shows a failed backup as a visible error with the mapped reason, never raw server text", async () => {
    stubApi({ handlers: { "/api/backup/backups": () => jsonResponse({ error: "destination_unavailable", message: "The backup destination is not available or not writable." }, 500) } });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Run backup now" }));
    expect(await screen.findByText(/Backup failed: The backup destination is not available or not writable\./)).toBeInTheDocument();
  });

  it("makes notification delivery testable and shows a broken channel as a failure", async () => {
    stubApi({ handlers: { "/api/backup/notifications/test": () => jsonResponse({ id: "n1", kind: "TestNotification", delivery: "Failed", deliveryFailureCode: "delivery_failed" }) } });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Send test notification" }));
    expect(await screen.findByText(/could NOT be delivered/)).toBeInTheDocument();
  });

  it("lists recent notifications with their delivery state", async () => {
    stubApi({ notifications: [{ id: "n1", backupRecordId: "b1", kind: "BackupFailed", message: "Backup b1 FAILED (destination_full)", createdAtUtc: "2026-10-01T02:00:00Z", delivery: "Failed", deliveryFailureCode: "delivery_failed" }] });
    renderPage();
    expect(await screen.findByText(/Backup b1 FAILED/)).toBeInTheDocument();
    expect(screen.getByText("Delivery failed")).toBeInTheDocument();
  });
});

describe("Recovery key setup", () => {
  async function openSetup() {
    stubApi({ status: status({ settings: settings({ recoveryKeyConfigured: false, scheduleEnabled: false }), lastSuccess: null }), history: [] });
  }

  it("requires the current password before calling the server and never asks the user to choose a passphrase", async () => {
    await openSetup();
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Set up recovery key" }));
    expect(screen.queryByLabelText("Recovery passphrase")).not.toBeInTheDocument(); // the server generates a strong one
    await userEvent.click(screen.getByRole("button", { name: "Create recovery key" }));
    expect(await screen.findByText("Enter your current password to confirm this action.")).toBeInTheDocument();
  });

  it("reports a wrong current password without creating a key", async () => {
    stubApi({
      status: status({ settings: settings({ recoveryKeyConfigured: false, scheduleEnabled: false }), lastSuccess: null }),
      history: [],
      handlers: { "/api/backup/recovery-key": () => jsonResponse({ error: "step_up_failed", message: "Your current password is required and was not accepted." }, 403) },
    });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Set up recovery key" }));
    await userEvent.type(screen.getByLabelText("Your current password"), "wrong");
    await userEvent.click(screen.getByRole("button", { name: "Create recovery key" }));
    expect(await screen.findByText("Your current password was not accepted.")).toBeInTheDocument();
    expect(screen.queryByLabelText("Recovery key file contents")).not.toBeInTheDocument();
  });

  it("shows the key exactly once, demands acknowledgement, clears it from the screen, and never writes it to browser storage", async () => {
    const privateKey = "-----BEGIN PGP PRIVATE KEY BLOCK-----\nSECRETSECRETSECRET\n-----END PGP PRIVATE KEY BLOCK-----";
    stubApi({
      status: status({ settings: settings({ recoveryKeyConfigured: false, scheduleEnabled: false }), lastSuccess: null }),
      history: [],
      handlers: { "/api/backup/recovery-key": () => jsonResponse({ recoveryKey: privateKey, passphrase: "GENERATED-PASSPHRASE-1234", fingerprint: "abcdef0123456789", warning: "store offline" }) },
    });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Set up recovery key" }));
    await userEvent.type(screen.getByLabelText("Your current password"), "my-password");
    await userEvent.click(screen.getByRole("button", { name: "Create recovery key" }));

    const keyBox = await screen.findByLabelText("Recovery key file contents");
    expect(keyBox).toHaveValue(privateKey);
    expect(screen.getByLabelText("Recovery passphrase (generated)")).toHaveValue("GENERATED-PASSPHRASE-1234");
    expect(screen.getByText(/only time the recovery key is shown/)).toBeInTheDocument();
    const done = screen.getByRole("button", { name: /Done - clear the key/ });
    expect(done).toBeDisabled(); // cannot leave without acknowledging

    await userEvent.click(screen.getByLabelText(/I have stored the recovery key file/));
    await userEvent.click(done);

    expect(screen.queryByLabelText("Recovery key file contents")).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain("SECRETSECRETSECRET");
    expect(screen.queryByLabelText("Recovery passphrase (generated)")).not.toBeInTheDocument();
    expect(document.body.innerHTML).not.toContain("GENERATED-PASSPHRASE-1234");
    expect(window.localStorage.length + window.sessionStorage.length).toBe(0);
  });

  it("explains that replacing the key only affects future backups", async () => {
    stubApi();
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Replace recovery key" }));
    expect(screen.getByText(/only affects FUTURE backups/)).toBeInTheDocument();
  });
});

describe("Disaster recovery from a retained archive (no backup history)", () => {
  const archive = (over: Partial<ArchiveInfo> = {}): ArchiveInfo => ({
    ref: "import:alveara-backup-lost.abk", location: "import", fileName: "alveara-backup-lost.abk", sizeBytes: 2_000_000, modifiedUtc: "2026-09-30T08:00:00Z",
    readable: true, recipientKeyId: "ABCDEF0123456789", keyMatchesConfiguredRecoveryKey: false, inHistory: false, ...over,
  });

  it("lists a retained archive and offers recovery even when the history is empty", async () => {
    stubApi({ history: [], archives: [archive(), archive({ ref: "import:junk.abk", fileName: "junk.abk", readable: false })] });
    renderPage();
    expect(await screen.findByRole("heading", { name: "Recover from a retained backup file" })).toBeInTheDocument();
    expect(await screen.findByText("alveara-backup-lost.abk")).toBeInTheDocument();
    expect(screen.getByText("Not in history")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Restore from alveara-backup-lost.abk" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Restore from junk.abk" })).toBeDisabled();
  });

  it("restores from the archive through the archive endpoints, with no verify-only shortcut, and shows the deployment-settings failure plainly", async () => {
    const failed = drill({
      id: "d9", backupRecordId: null, sourceKind: "Archive", archiveFileName: "alveara-backup-lost.abk", outcome: "Failed", failureCode: "deployment_settings_match_this_server",
      failureMessage: "This server is configured differently (PracticeTimeZone). Set PracticeTimeZone = Asia/Tokyo and DataProtection:ApplicationName = X, restart, and run recovery again.",
      checks: [{ name: "deployment_settings_match_this_server", passed: false, detail: "Set PracticeTimeZone = Asia/Tokyo and DataProtection:ApplicationName = X.", blocking: true }],
    });
    const api = stubApi({
      history: [], archives: [archive()],
      handlers: {
        "/api/backup/archives/preflight": () => jsonResponse({ canRestore: true, checks: [{ name: "archive_present", passed: true, detail: "The backup file is present.", blocking: true }] }),
        "/api/backup/archives/restore-drill": () => jsonResponse(failed, 422),
      },
    });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Restore from alveara-backup-lost.abk" }));
    const dialog = await screen.findByRole("dialog", { name: "Restore wizard" });
    await userEvent.type(within(dialog).getByLabelText("Or paste the key file contents"), "-----BEGIN PGP PRIVATE KEY BLOCK-----");
    await userEvent.type(within(dialog).getByLabelText("Recovery passphrase"), "generated passphrase value");
    await userEvent.type(within(dialog).getByLabelText("Your current password"), "my-password");
    await userEvent.click(within(dialog).getByRole("button", { name: /Check compatibility/ }));
    await within(dialog).findByText(/The backup file is present/);
    expect(within(dialog).queryByRole("button", { name: /Verify only/ })).not.toBeInTheDocument();

    await userEvent.click(within(dialog).getByLabelText(/I understand a restore drill/));
    await userEvent.click(within(dialog).getByRole("button", { name: /Restore into an isolated target/ }));

    expect(await within(dialog).findByText("Restore drill failed")).toBeInTheDocument();
    expect(within(dialog).getAllByText(/Set PracticeTimeZone = Asia\/Tokyo/).length).toBeGreaterThan(0);
    const call = api.calls.find((c) => c.url.includes("/api/backup/archives/restore-drill"))!;
    expect(call.body).toMatchObject({ archive: "import:alveara-backup-lost.abk", currentPassword: "my-password", passphrase: "generated passphrase value" });
    expect(api.calls.some((c) => c.url.includes("/api/backup/backups/"))).toBe(false);
  });
});

describe("Backup settings form", () => {
  it("validates inline, protects unsaved edits, and sends the version it read", async () => {
    const api = stubApi({ handlers: { "/api/backup/settings": () => jsonResponse(settings({ scheduleIntervalHours: 12 })) } });
    renderPage();
    const interval = await screen.findByLabelText("Hours between scheduled backups");
    await userEvent.clear(interval);
    await userEvent.type(interval, "0");
    expect(await screen.findByText("Unsaved changes")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save settings" }));
    expect(await screen.findByText("Enter a whole number of hours between 1 and 168.")).toBeInTheDocument();
    expect(api.calls.some((c) => c.method === "PUT")).toBe(false);

    await userEvent.clear(interval);
    await userEvent.type(interval, "12");
    await userEvent.click(screen.getByRole("button", { name: "Save settings" }));
    await waitFor(() => expect(api.calls.some((c) => c.method === "PUT")).toBe(true));
    expect(api.calls.find((c) => c.method === "PUT")!.body).toMatchObject({ scheduleIntervalHours: 12, retentionCount: 7, rowVersion: "AAAA", destinationDirectory: "D:\\Backups" });
  });

  it("rejects a relative backup folder and enabling the schedule without a recovery key", async () => {
    stubApi({ status: status({ settings: settings({ recoveryKeyConfigured: false, scheduleEnabled: false, destinationIsDefault: true }), lastSuccess: null }), history: [] });
    renderPage();
    await userEvent.click(await screen.findByLabelText("Run scheduled backups automatically"));
    await userEvent.type(screen.getByLabelText("Backup folder"), "relative/path");
    await userEvent.click(screen.getByRole("button", { name: "Save settings" }));
    expect(await screen.findByText("Set up the recovery key before enabling scheduled backups.")).toBeInTheDocument();
    expect(screen.getByText("Enter an absolute folder path, or leave blank for the default.")).toBeInTheDocument();
  });

  it("presents a stale settings save with the shared conflict banner", async () => {
    stubApi({ handlers: { "/api/backup/settings": () => jsonResponse({ error: "concurrency_conflict", entityType: "BackupSettings", entityId: "x" }, 409) } });
    renderPage();
    const retention = await screen.findByLabelText("Backups to keep");
    await userEvent.clear(retention);
    await userEvent.type(retention, "9");
    await userEvent.click(screen.getByRole("button", { name: "Save settings" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
  });

  it("shows the server's mapped reason when a setting is refused", async () => {
    stubApi({ handlers: { "/api/backup/settings": () => jsonResponse({ error: "invalid_destination", message: "x" }, 400) } });
    renderPage();
    const retention = await screen.findByLabelText("Backups to keep");
    await userEvent.clear(retention);
    await userEvent.type(retention, "9");
    await userEvent.click(screen.getByRole("button", { name: "Save settings" }));
    expect(await screen.findByText(/must be an absolute folder path outside the staging and restore areas/)).toBeInTheDocument();
  });
});

describe("History actions", () => {
  it("checks a file hash and reports the result", async () => {
    stubApi({ handlers: { "/verify-hash": () => jsonResponse(record({ verificationStatus: "HashVerified" })) } });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: /Check file hash/ }));
    expect(await screen.findByText("The backup file hash matches.")).toBeInTheDocument();
  });

  it("reports a hash mismatch as a failure with its reason", async () => {
    stubApi({ handlers: { "/verify-hash": () => jsonResponse(record({ verificationStatus: "VerificationFailed", verificationFailureCode: "hash_mismatch" })) } });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: /Check file hash/ }));
    expect((await screen.findAllByText(/does not match the hash recorded/)).length).toBeGreaterThan(0);
  });
});

describe("Restore wizard", () => {
  const preflightOk = { canRestore: true, checks: [{ name: "recovery_material_matches", passed: true, detail: "The recovery key and passphrase match this backup.", blocking: true }] };

  async function openWizard() {
    await userEvent.click(await screen.findByRole("button", { name: /Verify or restore/ }));
    return screen.findByRole("dialog", { name: "Restore wizard" });
  }

  async function fillMaterial() {
    await userEvent.type(screen.getByLabelText("Or paste the key file contents"), "-----BEGIN PGP PRIVATE KEY BLOCK-----");
    await userEvent.type(screen.getByLabelText("Recovery passphrase"), "my recovery passphrase");
    await userEvent.type(screen.getByLabelText("Your current password"), "my-password");
  }

  it("states up front that it never overwrites live data and offers only verify or an isolated restore", async () => {
    stubApi();
    renderPage();
    const dialog = await openWizard();
    expect(within(dialog).getByText(/never overwrites your live data/)).toBeInTheDocument();
    expect(within(dialog).queryByRole("button", { name: /overwrite|replace live|promote/i })).not.toBeInTheDocument();
  });

  it("requires the recovery material and password before checking anything", async () => {
    const api = stubApi();
    renderPage();
    await openWizard();
    await userEvent.click(screen.getByRole("button", { name: "Check compatibility and recovery material" }));
    expect(await screen.findByText(/Choose the recovery key file/)).toBeInTheDocument();
    expect(api.calls.some((c) => c.url.includes("/preflight"))).toBe(false);
  });

  it("shows failed preflight checks (wrong key) and blocks both verify and restore", async () => {
    stubApi({ handlers: { "/preflight": () => jsonResponse({ canRestore: false, checks: [{ name: "recovery_material_matches", passed: false, detail: "This recovery key does not belong to this backup.", blocking: true }] }) } });
    renderPage();
    await openWizard();
    await fillMaterial();
    await userEvent.click(screen.getByRole("button", { name: "Check compatibility and recovery material" }));
    expect(await screen.findByText(/does not belong to this backup/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Restore into an isolated target" })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Verify only/ })).toBeDisabled();
  });

  it("restores into an isolated target only after the explicit acknowledgement and lists every check", async () => {
    const api = stubApi({ handlers: { "/preflight": () => jsonResponse(preflightOk), "/b1/restore-drill": () => jsonResponse(drill(), 201) } });
    renderPage();
    await openWizard();
    await fillMaterial();
    await userEvent.click(screen.getByRole("button", { name: "Check compatibility and recovery material" }));
    const restore = await screen.findByRole("button", { name: "Restore into an isolated target" });
    expect(restore).toBeDisabled();
    await userEvent.click(screen.getByLabelText(/I understand a restore drill creates a new isolated database/));
    await userEvent.click(restore);

    expect(await screen.findByText("Restore drill succeeded")).toBeInTheDocument();
    expect(screen.getByText("AlveraRestore_abc")).toBeInTheDocument();
    expect(screen.getByText(/Every document\/blob listed in the manifest is present/)).toBeInTheDocument();
    expect(screen.getByText(/Your live data was not touched/)).toBeInTheDocument();
    const call = api.calls.find((c) => c.url.includes("/b1/restore-drill"))!;
    expect(call.body).toMatchObject({ currentPassword: "my-password", passphrase: "my recovery passphrase" });
  });

  it("verify-only verifies the contents without restoring and says it does not count toward trust", async () => {
    const api = stubApi({ handlers: { "/preflight": () => jsonResponse(preflightOk), "/api/backup/backups/b1/verify": () => jsonResponse(record({ verificationStatus: "FullyVerified" })) } });
    renderPage();
    await openWizard();
    await fillMaterial();
    await userEvent.click(screen.getByRole("button", { name: "Check compatibility and recovery material" }));
    await userEvent.click(await screen.findByRole("button", { name: /Verify only/ }));
    expect(await screen.findByText("Backup contents verified")).toBeInTheDocument();
    expect(screen.getByText(/does NOT prove the data restores/)).toBeInTheDocument();
    expect(api.calls.some((c) => c.url.includes("/b1/restore-drill"))).toBe(false);
  });

  it("shows a failed drill with the reason and its failed checks (a document missing from the backup set)", async () => {
    stubApi({
      handlers: {
        "/preflight": () => jsonResponse(preflightOk),
        "/b1/restore-drill": () =>
          jsonResponse(drill({ outcome: "Failed", failureCode: "documents_complete", failureMessage: "1 document/blob file(s) listed in the manifest are missing from the backup set.", checks: [{ name: "documents_complete", passed: false, detail: "1 document/blob file(s) listed in the manifest are missing from the backup set.", blocking: true }] }), 422),
      },
    });
    renderPage();
    await openWizard();
    await fillMaterial();
    await userEvent.click(screen.getByRole("button", { name: "Check compatibility and recovery material" }));
    await userEvent.click(await screen.findByLabelText(/I understand a restore drill/));
    await userEvent.click(screen.getByRole("button", { name: "Restore into an isolated target" }));

    expect(await screen.findByText("Restore drill failed")).toBeInTheDocument();
    expect(screen.getByText("Documents listed in the backup are missing from the backup set.")).toBeInTheDocument();
    expect(screen.getByText(/FAILED:/)).toBeInTheDocument();
  });

  it("clears the recovery material when the wizard closes: reopening starts from an empty form", async () => {
    let finish!: (r: Response) => void;
    stubApi({ handlers: { "/preflight": () => jsonResponse(preflightOk), "/b1/restore-drill": () => new Promise<Response>((resolve) => (finish = resolve)) as unknown as Response } });
    renderPage();
    await openWizard();
    await fillMaterial();
    await userEvent.click(screen.getByRole("button", { name: "Check compatibility and recovery material" }));
    await userEvent.click(await screen.findByLabelText(/I understand a restore drill/));
    await userEvent.click(screen.getByRole("button", { name: "Restore into an isolated target" }));
    expect(await screen.findByText(/Restoring into an isolated target/)).toBeInTheDocument();

    await act(async () => {
      finish(jsonResponse(drill(), 201));
    });
    // Closing and reopening starts from an empty form: nothing typed (key, passphrase, password) survives.
    await userEvent.click(await screen.findByRole("button", { name: "Close" }));
    await openWizard();
    expect(screen.getByLabelText("Or paste the key file contents")).toHaveValue("");
    expect(screen.getByLabelText("Recovery passphrase")).toHaveValue("");
    expect(screen.getByLabelText("Your current password")).toHaveValue("");
  });
});

describe("Restore drills", () => {
  it("removes an isolated target only with the current password and shows a wrong password plainly", async () => {
    let attempts = 0;
    const api = stubApi({
      drills: [drill()],
      handlers: {
        "/remove-target": () => (++attempts === 1 ? jsonResponse({ error: "step_up_failed", message: "no" }, 403) : jsonResponse(drill({ targetRemoved: true }))),
      },
    });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Remove target…" }));
    await userEvent.type(screen.getByLabelText("Your current password"), "wrong");
    await userEvent.click(screen.getByRole("button", { name: "Remove the restored copy" }));
    expect(await screen.findByText("Your current password was not accepted.")).toBeInTheDocument();

    await userEvent.clear(screen.getByLabelText("Your current password"));
    await userEvent.type(screen.getByLabelText("Your current password"), "right");
    await userEvent.click(screen.getByRole("button", { name: "Remove the restored copy" }));
    expect(await screen.findByText("The isolated restore target was removed.")).toBeInTheDocument();
    expect(api.calls.filter((c) => c.url.includes("/remove-target"))).toHaveLength(2);
  });
});

describe("Accessibility", () => {
  it("has no detectable axe violations with status, settings, recovery key, history and drills on screen", async () => {
    stubApi({ drills: [drill()], notifications: [{ id: "n1", backupRecordId: null, kind: "TestNotification", message: "test", createdAtUtc: "2026-10-01T02:00:00Z", delivery: "Delivered", deliveryFailureCode: null }] });
    const { container } = renderPage();
    await screen.findByRole("table", { name: undefined }).catch(() => screen.findByText(/Last successful backup/));
    await screen.findByText("Backup history");
    expect(await axe(container)).toHaveNoViolations();
  });
});
