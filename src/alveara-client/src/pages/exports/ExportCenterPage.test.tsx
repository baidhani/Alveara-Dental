import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { AuthProvider } from "../../contexts/AuthContext";
import type { ExportHistoryItem, ExportOverview } from "../../services/patientExportsApi";
import { ExportCenterPage } from "./ExportCenterPage";

// ALV-018-C01: the Export screen against a small in-memory server that answers like the real API (permissions, overview, history, patient search, the three downloads with their headers and stable error codes).

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const err = (status: number, error: string) => json({ error, message: "server words" }, status);

type Call = { method: string; url: string; init?: RequestInit };

const overview = (over: Partial<ExportOverview> = {}): ExportOverview => ({
  domains: [{ key: "registration", title: "Registration and contact details" }, { key: "billing", title: "Billing and payments" }],
  excluded: [{ key: "audit_log", reason: "The audit log belongs to the practice, not to a patient." }],
  maxPatients: 5000, maxPackageBytes: 2 * 1024 * 1024 * 1024, defaultFolderNaming: "IdOnly", allPatientsReadMode: "TableByTable",
  patientCount: 120, documentBytes: 5 * 1024 * 1024, allPatientsWithinLimits: true, practiceTimeZone: "America/Chicago", ...over,
});

const line = (over: Partial<ExportHistoryItem> = {}): ExportHistoryItem => ({
  exportId: "e-1", kind: "Package", scope: "OnePatient", patientId: "p1", includesDocumentFiles: true, requestedBy: "Dr. Okafor", requestedAtUtc: "2026-07-15T18:00:00+00:00",
  finishedAtUtc: "2026-07-15T18:00:09+00:00", result: "Complete", interrupted: false, patientCount: 1, fileCount: 30, sizeBytes: 3 * 1024 * 1024, sha256: "abcdef0123456789".repeat(4), problemCount: 0, ...over,
});

const ann = { id: "p1", firstName: "Ann", middleName: null, lastName: "Rivera", dateOfBirth: "1980-01-01", age: 46, sex: null, phone: "555-0100", city: "", state: "", isActive: true };

let permissions: string[];
let ov: ExportOverview;
let history: ExportHistoryItem[];
let calls: Call[];
let reply: (call: Call) => Response | Promise<Response> | null;

const ALL = ["ExportPatientData", "ExportAllPatientData", "ExportPatientReadableRecord", "ViewPatientRecords"];

beforeEach(() => {
  permissions = ALL; ov = overview(); history = []; calls = []; reply = () => null;
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url);
    const call = { method: init?.method ?? "GET", url: u, init };
    if (u.includes("/api/auth/permissions")) return Promise.resolve(json({ username: "admin", role: "Admin", permissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }));
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "t" }));
    calls.push(call);
    const custom = reply(call);
    if (custom) return Promise.resolve(custom);
    if (u.startsWith("/api/patient-exports/overview")) return Promise.resolve(json(ov));
    if (u.startsWith("/api/patient-exports/history")) {
      const query = new URL(u, "http://x").searchParams;
      const before = query.get("before");
      const older = before ? history.filter((h) => h.requestedAtUtc < before) : history;
      return Promise.resolve(json(older.slice(0, Number(query.get("limit") ?? 25))));               // newest first, a page at a time, like the real server
    }
    if (u.startsWith("/api/patients?")) return Promise.resolve(json([ann]));
    return Promise.resolve(err(404, "not_found"));
  }));
  Object.defineProperty(URL, "createObjectURL", { configurable: true, writable: true, value: vi.fn(() => "blob:test") });
  Object.defineProperty(URL, "revokeObjectURL", { configurable: true, writable: true, value: vi.fn() });
  downloads = [];
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) { downloads.push(this.download); });
});
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

let downloads: string[];

const zip = (headers: Record<string, string> = {}) => new Response(new Blob(["zip"]), {
  status: 200, headers: { "Content-Type": "application/zip", "Content-Disposition": "attachment; filename=alveara-patient-package-abc.zip", "X-Export-Id": "e-9", "X-Export-Result": "Complete", "X-Export-Problems": "0", ...headers },
});
const exportCalls = () => calls.filter((c) => c.method === "POST");
const bodyOf = (c: Call) => (c.init?.body ? JSON.parse(c.init.body as string) : undefined);

function renderPage() {
  return render(<MemoryRouter><AuthProvider><ExportCenterPage /></AuthProvider></MemoryRouter>);
}

async function choosePatient(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText("Patient"), "Ann");
  await user.click(await screen.findByRole("button", { name: /Ann Rivera/ }));
}

describe("what is offered", () => {
  it("offers one patient and all patients, and the package or the readable record, to someone who holds all the permissions", async () => {
    renderPage();

    expect(await screen.findByRole("radio", { name: "One patient" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "All patients" })).not.toBeChecked();
    expect(screen.getByRole("radio", { name: /Complete package/ })).toBeChecked();
    expect(screen.getByRole("radio", { name: /Readable record only/ })).toBeInTheDocument();
  });

  it("offers only one patient's package to someone who holds only that permission", async () => {
    permissions = ["ExportPatientData"];
    renderPage();

    expect(await screen.findByLabelText("Patient")).toBeInTheDocument();
    expect(screen.queryByRole("radio", { name: "All patients" })).not.toBeInTheDocument();
    expect(screen.queryByRole("radio", { name: /Readable record only/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create package" })).toBeDisabled();             // no patient chosen yet
    expect(screen.getByText(/You see the exports you made\./)).toBeInTheDocument();
  });

  it("offers only the readable record to someone who holds only that permission, with no package options", async () => {
    permissions = ["ExportPatientReadableRecord"];
    renderPage();

    expect(await screen.findByRole("button", { name: "Download readable record" })).toBeDisabled();
    expect(screen.queryByLabelText(/Include document files/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Patient folder names")).not.toBeInTheDocument();
  });

  it("says so when the person holds no export permission, and offers nothing", async () => {
    permissions = ["ViewPatientRecords"];
    renderPage();

    expect(await screen.findByText("You do not hold any export permission.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /package|readable/i })).not.toBeInTheDocument();
  });

  it("lists what a package holds and what it leaves out, with the reason for each exclusion", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByText("What is in a package and what is left out"));

    expect(screen.getByText("Registration and contact details")).toBeVisible();
    expect(screen.getByText(/The audit log belongs to the practice/)).toBeVisible();
    expect(screen.getByText("Audit log.")).toBeVisible();
  });
});

describe("creating a package for one patient", () => {
  it("sends the choices, saves the file under the server's name, says it was logged and refreshes the history", async () => {
    reply = (c) => (c.method === "POST" ? zip() : null);
    const user = userEvent.setup();
    renderPage();

    await choosePatient(user);
    await user.click(screen.getByRole("button", { name: "Create package" }));

    expect(await screen.findByText("Saved alveara-patient-package-abc.zip. The export was logged (export e-9).")).toBeInTheDocument();
    const [post] = exportCalls();
    expect(post.url).toBe("/api/patient-exports/patients/p1/package");
    expect(bodyOf(post)).toEqual({ includeDocumentFiles: true });                                 // folder naming untouched, so the practice default applies
    expect(new Headers(post.init?.headers).get("X-CSRF-Token")).toBe("t");
    expect(downloads).toEqual(["alveara-patient-package-abc.zip"]);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:test");
    await waitFor(() => expect(calls.filter((c) => c.url.startsWith("/api/patient-exports/history")).length).toBe(2));
  });

  it("sends the document choice and the folder naming when they are changed, and warns that names will show in file listings", async () => {
    reply = (c) => (c.method === "POST" ? zip() : null);
    const user = userEvent.setup();
    renderPage();

    await choosePatient(user);
    expect(screen.queryByText(/Folder names will contain patient names/)).not.toBeInTheDocument();
    await user.click(screen.getByLabelText(/Include document files/));
    await user.selectOptions(screen.getByLabelText("Patient folder names"), "NameAndId");
    expect(screen.getByText(/Folder names will contain patient names/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Create package" }));

    await screen.findByText(/Saved alveara-patient-package-abc.zip/);
    expect(bodyOf(exportCalls()[0])).toEqual({ includeDocumentFiles: false, folderNaming: "NameAndId" });
  });

  it("asks for the readable record alone when that is chosen, with no options in the request", async () => {
    reply = (c) => (c.method === "POST" ? new Response(new Blob(["<html>"]), { status: 200, headers: { "Content-Disposition": "attachment; filename=alveara-readable-record-x.html", "X-Export-Id": "e-5" } }) : null);
    const user = userEvent.setup();
    renderPage();

    await choosePatient(user);
    await user.click(screen.getByRole("radio", { name: /Readable record only/ }));
    expect(screen.queryByLabelText("Patient folder names")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Download readable record" }));

    await screen.findByText(/Saved alveara-readable-record-x.html/);
    expect(exportCalls()[0].url).toBe("/api/patient-exports/patients/p1/readable-record");
    expect(bodyOf(exportCalls()[0])).toBeUndefined();
  });

  it("can be started from the keyboard alone", async () => {
    reply = (c) => (c.method === "POST" ? zip() : null);
    const user = userEvent.setup();
    renderPage();
    await choosePatient(user);

    screen.getByRole("button", { name: "Create package" }).focus();
    await user.keyboard("{Enter}");

    await screen.findByText(/Saved alveara-patient-package-abc.zip/);
  });
});

describe("creating a package for every patient", () => {
  const chooseAll = async (user: ReturnType<typeof userEvent.setup>) => user.click(await screen.findByRole("radio", { name: "All patients" }));

  it("shows the counts and limits and how the database is read, and creates the package", async () => {
    reply = (c) => (c.method === "POST" ? zip({ "Content-Disposition": "attachment; filename=alveara-all-patients-package-1.zip" }) : null);
    const user = userEvent.setup();
    renderPage();

    await chooseAll(user);
    expect(screen.getByText(/120 patients, with 5.0 MB of document files/)).toBeInTheDocument();
    expect(screen.getByText(/one table after another/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Create package for all patients" }));

    await screen.findByText(/Saved alveara-all-patients-package-1.zip/);
    expect(exportCalls()[0].url).toBe("/api/patient-exports/all-patients/package");
  });

  it("says the whole database is read as it was at one moment when the server can do that", async () => {
    ov = overview({ allPatientsReadMode: "Snapshot" });
    const user = userEvent.setup();
    renderPage();

    await chooseAll(user);

    expect(screen.getByText("The export reads the whole database as it was at one moment.")).toBeInTheDocument();
  });

  it("disables the button with the reason when there are more patients than one export may carry", async () => {
    ov = overview({ patientCount: 5001, allPatientsWithinLimits: false });
    const user = userEvent.setup();
    renderPage();

    await chooseAll(user);

    expect(screen.getByRole("button", { name: "Create package for all patients" })).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent(/more patients than one export may carry/);
  });

  it("disables the button when the documents are too big, until the document files are turned off", async () => {
    ov = overview({ documentBytes: 3 * 1024 * 1024 * 1024, allPatientsWithinLimits: false });
    const user = userEvent.setup();
    renderPage();

    await chooseAll(user);
    expect(screen.getByRole("button", { name: "Create package for all patients" })).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent(/Turn off “Include document files”/);
    await user.click(screen.getByLabelText(/Include document files/));

    expect(screen.getByRole("button", { name: "Create package for all patients" })).toBeEnabled();
  });
});

describe("what happens after", () => {
  it("warns that a Partial package is incomplete and where to look", async () => {
    reply = (c) => (c.method === "POST" ? zip({ "X-Export-Result": "Partial", "X-Export-Problems": "2" }) : null);
    const user = userEvent.setup();
    renderPage();

    await choosePatient(user);
    await user.click(screen.getByRole("button", { name: "Create package" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("This package is incomplete.");
    expect(alert).toHaveTextContent("2 things could not be included");
    expect(alert).toHaveTextContent("manifest.json");
    expect(screen.getByText(/Saved alveara-patient-package-abc.zip/)).toBeInTheDocument();      // it was still saved and logged
  });

  it.each([
    ["export_in_progress", 409, /Another all-patients export is running/],
    ["permission_revoked", 403, /no longer have permission/],
    ["package_too_large", 413, /larger than one export may carry/],
    ["patient_merged", 409, /merged into another patient record/],
    ["export_failed", 500, /nothing was delivered/],
  ])("keeps the reason on the screen, saves nothing and refreshes the history when the server says %s", async (code, status, words) => {
    reply = (c) => (c.method === "POST" ? err(status, code) : null);
    const user = userEvent.setup();
    renderPage();

    await choosePatient(user);
    await user.click(screen.getByRole("button", { name: "Create package" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(words);
    expect(downloads).toEqual([]);
    expect(screen.queryByText(/Saved /)).not.toBeInTheDocument();
    await waitFor(() => expect(calls.filter((c) => c.url.startsWith("/api/patient-exports/history")).length).toBe(2));   // a failed export leaves a Failed line
    expect(screen.getByRole("button", { name: "Create package" })).toBeEnabled();
  });

  it("shows that it is working, lets the person cancel, and says nothing was saved", async () => {
    reply = (c) => c.method === "POST" ? new Promise<Response>((_, reject) => {
      (c.init!.signal as AbortSignal).addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
    }) : null;
    const user = userEvent.setup();
    renderPage();
    await choosePatient(user);

    await user.click(screen.getByRole("button", { name: "Create package" }));
    expect(screen.getByRole("button", { name: "Creating…" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Cancel" }));

    expect(await screen.findByText(/Cancelled. Nothing was saved/)).toBeInTheDocument();
    expect(downloads).toEqual([]);
    expect(screen.queryByRole("button", { name: "Cancel" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create package" })).toBeEnabled();
  });
});

describe("the history", () => {
  it("shows each export with its result in words, the size, a short file check and the time in the practice time zone", async () => {
    history = [
      line({ exportId: "e-1" }),
      line({ exportId: "e-2", result: "Partial", problemCount: 2, requestedAtUtc: "2026-07-14T18:00:00+00:00" }),
      line({ exportId: "e-3", result: "Failed", sha256: null, sizeBytes: 0, requestedAtUtc: "2026-07-13T18:00:00+00:00" }),
      line({ exportId: "e-4", result: "Started", interrupted: true, sha256: null, sizeBytes: 0, requestedAtUtc: "2026-07-12T18:00:00+00:00" }),
      line({ exportId: "e-5", result: "Started", sha256: null, sizeBytes: 0, requestedAtUtc: "2026-07-11T18:00:00+00:00" }),
      line({ exportId: "e-6", scope: "AllPatients", patientId: null, patientCount: 50, kind: "Package", includesDocumentFiles: false, requestedAtUtc: "2026-07-10T18:00:00+00:00" }),
      line({ exportId: "e-7", kind: "ReadableRecord", includesDocumentFiles: false, requestedAtUtc: "2026-07-09T18:00:00+00:00" }),
    ];
    renderPage();

    const table = await screen.findByRole("table", { name: "Export history, newest first" });
    const rows = within(table).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByText("Complete")).toBeInTheDocument();
    expect(rows[0]).toHaveTextContent("1:00 PM CDT");                                           // 18:00 UTC in the practice's zone, named
    expect(rows[0]).toHaveTextContent("3.0 MB");
    expect(within(rows[0]).getByText("abcdef012345")).toHaveAttribute("title", "abcdef0123456789".repeat(4));
    expect(rows[1]).toHaveTextContent("Partial");
    expect(rows[1]).toHaveTextContent("2 problems");
    expect(rows[2]).toHaveTextContent("Failed");
    expect(rows[3]).toHaveTextContent("Interrupted");
    expect(rows[4]).toHaveTextContent("In progress");
    expect(rows[5]).toHaveTextContent("All patients (50)");
    expect(rows[5]).toHaveTextContent("Package");
    expect(rows[5]).not.toHaveTextContent("with documents");
    expect(rows[6]).toHaveTextContent("Readable record");
    expect(rows[0]).toHaveTextContent("Package with documents");
    expect(within(rows[2]).getAllByText("-").length).toBeGreaterThan(0);                       // a failed export has no size and no file check
  });

  it("links a one-patient line to the record only for someone who may open records, and never names the patient", async () => {
    history = [line()];
    const { unmount } = renderPage();
    expect(await screen.findByRole("link", { name: "Open the patient" })).toHaveAttribute("href", "/patients/p1");
    unmount();

    permissions = ["ExportPatientData"];
    renderPage();
    await screen.findByRole("table");
    expect(screen.queryByRole("link", { name: "Open the patient" })).not.toBeInTheDocument();
    expect(screen.getByText("One patient")).toBeInTheDocument();
  });

  it("says whose exports are shown", async () => {
    renderPage();
    expect(await screen.findByText(/You see everyone's exports\. Times are in the practice time zone \(America\/Chicago\)\./)).toBeInTheDocument();
  });

  it("says there are no exports yet, rather than showing an empty table", async () => {
    renderPage();
    expect(await screen.findByText("No exports yet.")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("loads older exports from the last line's time and adds them below", async () => {
    history = Array.from({ length: 25 }, (_, i) => line({ exportId: `n-${i}`, requestedAtUtc: `2026-07-${String(30 - i).padStart(2, "0")}T18:00:00+00:00` }));
    history.push(line({ exportId: "older-1", requestedAtUtc: "2026-06-01T18:00:00+00:00" }));
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("button", { name: "Load older exports" }));

    await waitFor(() => expect(within(screen.getByRole("table")).getAllByRole("row")).toHaveLength(1 + 25 + 1));
    const historyCalls = calls.filter((c) => c.url.startsWith("/api/patient-exports/history"));
    expect(new URL(historyCalls[historyCalls.length - 1].url, "http://x").searchParams.get("before")).toBe("2026-07-06T18:00:00+00:00");
    expect(screen.queryByRole("button", { name: "Load older exports" })).not.toBeInTheDocument();      // fewer than a full page came back
  });

  it("says when the history cannot be read and offers a retry, instead of showing an empty list", async () => {
    reply = (c) => (c.url.startsWith("/api/patient-exports/history") ? err(500, "boom") : null);
    renderPage();

    expect(await screen.findByText("Could not load the export history")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
    expect(screen.queryByText("No exports yet.")).not.toBeInTheDocument();
  });
});

describe("the screen as a whole", () => {
  it("has no accessibility violations with the form, the details and a full history", async () => {
    history = [line(), line({ exportId: "e-2", result: "Partial", problemCount: 1 }), line({ exportId: "e-3", result: "Failed", sha256: null })];
    const user = userEvent.setup();
    const { container } = renderPage();
    await screen.findByRole("table");
    await user.click(screen.getByText("What is in a package and what is left out"));

    expect(await axe(container)).toHaveNoViolations();
  });

  it("says the export details could not be loaded when the overview fails", async () => {
    reply = (c) => (c.url.startsWith("/api/patient-exports/overview") ? err(500, "boom") : null);
    renderPage();

    expect(await screen.findByText("Could not load the export details")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Create package/ })).not.toBeInTheDocument();
  });
});
