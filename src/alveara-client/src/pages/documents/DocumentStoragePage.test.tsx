import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import type { OrphanFile, StorageProblem, StorageReport, StorageSummary } from "../../services/documentsApi";
import { DocumentStoragePage } from "./DocumentStoragePage";

/**
 * ALV-010-C01: the storage page against a scripted API. The checks and the removal rules are proven by the backend tests; what is proven here is the screen: that it shows the truth (counts, the files that need
 * attention, the files nothing points to), that a check says what it found, that nothing is removed without a deliberate selection and a confirmation, that every outcome is reported in words, and that errors are
 * shown rather than hidden.
 */
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const problem = (id: string, name: string, status: StorageProblem["status"]): StorageProblem => ({ documentId: id, patientId: "p1", fileName: name, status, checkedAtUtc: "2030-03-11T10:00:00Z" });
const orphan = (id: string, size = 4): OrphanFile => ({ blobId: id, sizeBytes: size, lastWriteUtc: "2030-03-01T10:00:00Z" });
const summaryOf = (over: Partial<StorageSummary> = {}): StorageSummary => ({ documents: 5, ok: 3, missing: 1, corrupt: 1, neverChecked: 0, problems: [problem("d1", "gone.png", "Missing"), problem("d2", "bad.png", "Corrupt")], orphans: [orphan("b1"), orphan("b2", 2048)], ...over });

interface Call { url: string; method: string; headers: Record<string, string>; body: Record<string, unknown> | null }
interface Script {
  summary: StorageSummary; loadFails: boolean; check: () => Response; checkOne: (id: string) => Response; remove: (b: Record<string, unknown>) => Response;
}

function stub(over: Partial<Script> = {}) {
  const script: Script = {
    summary: summaryOf(), loadFails: false,
    check: () => json({ checked: 5, ok: 3, missing: 1, corrupt: 1, skipped: 0, problems: [], orphans: [], ranAtUtc: "2030-03-12T10:00:00Z" } satisfies StorageReport),
    checkOne: (id) => json({ documentId: id, status: "Ok", checkedAtUtc: "2030-03-12T10:00:00Z" }),
    remove: (b) => json({ results: (b.blobIds as string[]).map((blobId) => ({ blobId, outcome: "Removed" })), removed: (b.blobIds as string[]).length }),
    ...over,
  };
  const calls: Call[] = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    calls.push({ url: u, method, headers: (init?.headers ?? {}) as Record<string, string>, body });
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (u.endsWith("/api/document-storage") && method === "GET") return Promise.resolve(script.loadFails ? json({ error: "server_error" }, 500) : json(script.summary));
    if (u.endsWith("/api/document-storage/check")) return Promise.resolve(script.check());
    if (u.endsWith("/check")) return Promise.resolve(script.checkOne(u.split("/").slice(-2)[0]));
    if (u.endsWith("/orphans/remove")) return Promise.resolve(script.remove(body!));
    return Promise.resolve(json({}));
  }));
  return { script, calls, sent: (suffix: string) => calls.filter((c) => c.method === "POST" && c.url.endsWith(suffix)), loads: () => calls.filter((c) => c.method === "GET" && c.url.endsWith("/api/document-storage")).length };
}

async function open() {
  const view = render(<MemoryRouter><DocumentStoragePage /></MemoryRouter>);
  await screen.findByRole("heading", { name: "Document storage" });
  await waitFor(() => expect(screen.queryByText(/Loading document storage/i)).toBeNull());
  return view;
}

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("what it shows", () => {
  it("shows the counts, the files that need attention in words and the files nothing points to", async () => {
    stub();
    await open();
    const counts = screen.getByRole("table", { name: "Storage status" });
    expect(within(counts).getByRole("row", { name: /Intact\s*3/ })).toBeInTheDocument();
    expect(within(counts).getByRole("row", { name: /Missing\s*1/ })).toBeInTheDocument();
    expect(within(counts).getByRole("row", { name: /Damaged\s*1/ })).toBeInTheDocument();
    expect(within(counts).getByRole("row", { name: /Not checked yet\s*0/ })).toBeInTheDocument();
    const attention = screen.getByRole("table", { name: "Files that need attention" });
    expect(within(attention).getByRole("row", { name: /gone\.png.*Missing/ })).toBeInTheDocument();
    expect(within(attention).getByRole("row", { name: /bad\.png.*Damaged/ })).toBeInTheDocument();
    expect(within(attention).getAllByRole("link", { name: /Open documents of the patient/ })[0]).toHaveAttribute("href", "/patients/p1/documents");
    expect(screen.getByRole("list", { name: "Stored files nothing points to" }).querySelectorAll("li")).toHaveLength(2);
  });

  it("says plainly when nothing needs attention and when no stored file is unreferenced", async () => {
    stub({ summary: summaryOf({ missing: 0, corrupt: 0, problems: [], orphans: [] }) });
    await open();
    expect(screen.getByText("No file needs attention")).toBeInTheDocument();
    expect(screen.getByText("No stored file is unreferenced")).toBeInTheDocument();
  });

  it("says plainly when it cannot be loaded", async () => {
    stub({ loadFails: true });
    render(<MemoryRouter><DocumentStoragePage /></MemoryRouter>);
    expect(await screen.findByText("Could not load the document storage")).toBeInTheDocument();
  });
});

describe("checking", () => {
  it("runs the check of every stored file, says what it found and shows the new picture", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    const loads = api.loads();
    await user.click(screen.getByRole("button", { name: "Check all stored files" }));
    await waitFor(() => expect(api.sent("/api/document-storage/check")).toHaveLength(1));
    expect(api.sent("/api/document-storage/check")[0].headers["X-CSRF-Token"]).toBe("csrf");
    expect(await screen.findByRole("status")).toHaveTextContent("Checked 5 documents: 3 intact, 1 missing, 1 damaged.");
    await waitFor(() => expect(api.loads()).toBeGreaterThan(loads));
  });

  it("mentions files that could not be read just now", async () => {
    stub({ check: () => json({ checked: 5, ok: 2, missing: 1, corrupt: 1, skipped: 1, problems: [], orphans: [], ranAtUtc: "2030-03-12T10:00:00Z" }) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Check all stored files" }));
    expect(await screen.findByRole("status")).toHaveTextContent("1 could not be read just now and was left as it was");
  });

  it("is busy while it runs, says that it reads every file and cannot be started twice", async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((r) => { release = r; });
    const api = stub();
    const original = vi.mocked(fetch).getMockImplementation()!;
    vi.mocked(fetch).mockImplementation(async (url, init) => { if (String(url).endsWith("/document-storage/check")) await gate; return original(url, init); });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Check all stored files" }));
    expect(screen.getByRole("button", { name: "Checking…" })).toBeDisabled();
    expect(screen.getByText(/reads every stored file/)).toBeInTheDocument();
    release();
    await screen.findByRole("status");
    expect(api.sent("/api/document-storage/check")).toHaveLength(1);
  });

  it("shows a failed check as a failure and not as an empty result", async () => {
    stub({ check: () => json({ error: "save_failed", message: "The result of the storage check could not be saved. Try again; if it keeps failing, contact support." }, 503) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Check all stored files" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("could not be saved");
  });

  it("checks one file again and says the result", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Check gone.png again" }));
    await waitFor(() => expect(api.sent("/documents/d1/check")).toHaveLength(1));
    expect(await screen.findByRole("status")).toHaveTextContent("gone.png is intact.");
  });

  it("explains a file that could not be read for a single check", async () => {
    stub({ checkOne: () => json({ error: "file_unreadable", message: "The stored file could not be read just now. Try the check again in a moment." }, 503) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Check gone.png again" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("could not be read just now");
  });
});

describe("files nothing points to", () => {
  const list = () => screen.getByRole("list", { name: "Stored files nothing points to" });

  it("removes only the files that were selected, after a confirmation, and says what happened to each", async () => {
    const api = stub({ remove: () => json({ results: [{ blobId: "b1", outcome: "Removed" }, { blobId: "b2", outcome: "TooNew" }], removed: 1 }) });
    const user = userEvent.setup();
    await open();
    expect(screen.getByRole("button", { name: "Remove selected files" })).toBeDisabled();
    await user.click(within(list()).getByRole("checkbox", { name: /b1/ }));
    await user.click(screen.getByRole("button", { name: "Remove selected files" }));
    expect(api.sent("/orphans/remove")).toHaveLength(0);                                                      // nothing yet: a confirmation comes first
    const confirm = screen.getByRole("alertdialog", { name: "Remove 1 stored file?" });
    expect(confirm).toHaveTextContent("cannot be undone");
    await user.click(within(confirm).getByRole("button", { name: "Remove 1 file" }));
    await waitFor(() => expect(api.sent("/orphans/remove")).toHaveLength(1));
    expect(api.sent("/orphans/remove")[0].body).toEqual({ blobIds: ["b1"] });
    expect(await screen.findByRole("status")).toHaveTextContent("Removed 1 file. 1 was too new to remove (it may be an import in progress).");
  });

  it("cancelling the confirmation removes nothing", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await user.click(within(list()).getByRole("checkbox", { name: /b2/ }));
    await user.click(screen.getByRole("button", { name: "Remove selected files" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("alertdialog")).toBeNull();
    expect(api.sent("/orphans/remove")).toHaveLength(0);
    expect(within(list()).getByRole("checkbox", { name: /b2/ })).toBeChecked();
  });

  it("selects every file with one button and sends them all", async () => {
    const api = stub();
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Select all" }));
    for (const box of within(list()).getAllByRole("checkbox")) expect(box).toBeChecked();
    await user.click(screen.getByRole("button", { name: "Remove selected files" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Remove 2 files" }));
    await waitFor(() => expect(api.sent("/orphans/remove")[0].body).toEqual({ blobIds: ["b1", "b2"] }));
  });

  it("names each kind of outcome in words", async () => {
    stub({ remove: () => json({ results: [{ blobId: "b1", outcome: "AlreadyGone" }, { blobId: "b2", outcome: "StillReferenced" }, { blobId: "b3", outcome: "Failed" }], removed: 0 }), summary: summaryOf({ orphans: [orphan("b1"), orphan("b2"), orphan("b3")] }) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Select all" }));
    await user.click(screen.getByRole("button", { name: "Remove selected files" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Remove 3 files" }));
    const status = await screen.findByRole("status");
    expect(status).toHaveTextContent("Removed 0 files.");
    expect(status).toHaveTextContent("1 was already gone");
    expect(status).toHaveTextContent("1 is now used by a document and was kept");
    expect(status).toHaveTextContent("1 could not be removed and was kept");
  });

  it("refuses to send more than 500 files at once and says so", async () => {
    const many = Array.from({ length: 501 }, (_, i) => orphan(`b${i}`));
    const api = stub({ summary: summaryOf({ orphans: many }) });
    const user = userEvent.setup();
    await open();
    await user.click(screen.getByRole("button", { name: "Select all" }));
    expect(screen.getByText("Select at most 500 files at a time.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Remove selected files" })).toBeDisabled();
    expect(api.sent("/orphans/remove")).toHaveLength(0);
  });

  it("shows a refused removal as a failure and keeps the selection", async () => {
    stub({ remove: () => json({ error: "metadata_invalid", message: "That entry needs correcting. Nothing was removed." }, 400) });
    const user = userEvent.setup();
    await open();
    await user.click(within(list()).getByRole("checkbox", { name: /b1/ }));
    await user.click(screen.getByRole("button", { name: "Remove selected files" }));
    await user.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Remove 1 file" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Nothing was removed");
    expect(within(list()).getByRole("checkbox", { name: /b1/ })).toBeChecked();
  });
});

describe("accessibility", () => {
  it("has no detectable violations with the report and a confirmation open", async () => {
    stub();
    const user = userEvent.setup();
    const { container } = await open();
    await user.click(within(screen.getByRole("list", { name: "Stored files nothing points to" })).getByRole("checkbox", { name: /b1/ }));
    await user.click(screen.getByRole("button", { name: "Remove selected files" }));
    expect(await axe(container)).toHaveNoViolations();
  });
});
