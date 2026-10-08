import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { MemoryRouter } from "react-router-dom";
import { ProcedureCatalogPage } from "./ProcedureCatalogPage";
import { AuthProvider } from "../contexts/AuthContext";
import { NotificationProvider } from "../components/Notification";
import type { ProcedureSummary, ProcedureVersion } from "../services/proceduresApi";
import { formatFee, parseFee } from "./procedures/procedureText";

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const version = (over: Partial<ProcedureVersion> = {}): ProcedureVersion => ({
  versionId: "v1", procedureId: "p1", versionNumber: 1, description: "Adult cleaning", category: "Preventive", scope: "WholeMouth", dentition: "Both", fee: 95.5, currency: "USD",
  sourceName: null, sourceVersion: null, effectiveFrom: "2030-01-01", validThrough: null, reason: null, createdByName: "Dr. Okafor", createdAtUtc: "2030-01-01T12:00:00Z", ...over,
});
const summary = (over: Partial<ProcedureSummary> = {}): ProcedureSummary => ({
  id: "p1", codeSystem: "Local", code: "CLEAN-1", isActive: true, status: "Active", asOf: "2030-03-15", currentVersionNumber: 1, version: version(), rowVersion: "RV1", createdAtUtc: "2030-01-01T12:00:00Z", ...over,
});

interface Api {
  rows: ProcedureSummary[];
  permissions: string[];
  usageCount: number;
  calls: { url: string; method: string; body: Record<string, unknown> | null }[];
  respond: Record<string, (body: Record<string, unknown> | null) => Response>;
}

function stubApi(over: Partial<Api> = {}): Api {
  const api: Api = { rows: [summary()], permissions: ["ViewBilling", "ManageBilling"], usageCount: 0, calls: [], respond: {}, ...over };
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    api.calls.push({ url: u, method, body });
    for (const [fragment, handler] of Object.entries(api.respond)) if (u.includes(fragment)) return Promise.resolve(handler(body));
    if (u.includes("/api/auth/permissions")) return Promise.resolve(json({ username: "billing", role: "Billing", permissions: api.permissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }));
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (u.includes("/usage")) return Promise.resolve(json({ procedureId: "p1", count: api.usageCount, bySource: [{ source: "Odontogram links", count: api.usageCount }] }));
    if (u.includes("/history")) return Promise.resolve(json([{ eventNumber: 1, changeType: "Created", versionNumber: 1, reason: null, actorName: "Dr. Okafor", occurredAtUtc: "2030-01-01T12:00:00Z" }]));
    if (/\/api\/procedures\/[^/?]+$/.test(u) && method === "GET") return Promise.resolve(json({ summary: api.rows[0], versions: [api.rows[0].version] }));
    if (u.includes("/api/procedures") && method === "GET") return Promise.resolve(json(api.rows));
    if (method === "POST") return Promise.resolve(json({ summary: api.rows[0], versions: [api.rows[0].version] }));
    return Promise.resolve(json({}));
  }));
  return api;
}

function renderPage() {
  return render(
    <MemoryRouter>
      <NotificationProvider>
        <AuthProvider>
          <ProcedureCatalogPage />
        </AuthProvider>
      </NotificationProvider>
    </MemoryRouter>
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

const statusLine = () => document.querySelector(".alv-clinical__status") as HTMLElement;
const addForm = () => within(screen.getByRole("form", { name: "Add a procedure" }));
const posts = (api: Api) => api.calls.filter((c) => c.method === "POST" && !c.url.includes("csrf"));

describe("fee text", () => {
  it("accepts dollars with up to two decimals, a dollar sign and thousands separators", () => {
    expect(parseFee("95")).toEqual({ fee: 95 });
    expect(parseFee("$1,250.50")).toEqual({ fee: 1250.5 });
    expect(parseFee("0")).toEqual({ fee: 0 });
    expect(parseFee("1000000")).toEqual({ fee: 1000000 });
  });
  it("refuses an empty, negative, over-precise or oversized fee in words", () => {
    for (const bad of ["", "  ", "-5", "12.345", "abc", "1000000.01"]) expect("error" in parseFee(bad)).toBe(true);
    expect(formatFee(95.5)).toBe("$95.50");
  });
});

describe("Procedure catalog page", () => {
  it("lists procedures with code, description, fee, code system, category and where each applies", async () => {
    stubApi();
    renderPage();
    const list = await screen.findByRole("list", { name: "Procedures" });
    expect(within(list).getByText("CLEAN-1")).toBeInTheDocument();
    expect(list).toHaveTextContent("$95.50");
    expect(list).toHaveTextContent("Adult cleaning");
    expect(list).toHaveTextContent("Practice code");
    expect(list).toHaveTextContent("Preventive");
    expect(list).toHaveTextContent("Whole mouth");
  });

  it("shows a read-only view to someone who can see billing but not change it", async () => {
    stubApi({ permissions: ["ViewBilling"] });
    renderPage();
    await screen.findByRole("list", { name: "Procedures" });
    expect(screen.queryByRole("button", { name: "Add a procedure" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Change procedure/ })).not.toBeInTheDocument();
    expect(statusLine()).toHaveTextContent("your role cannot change it");
  });

  it("says permission is needed, and loads nothing, for someone who cannot see billing", async () => {
    const api = stubApi({ permissions: [] });
    renderPage();
    expect((await screen.findAllByText(/permission/i)).length).toBeGreaterThan(0);
    expect(api.calls.some((c) => c.url.startsWith("/api/procedures"))).toBe(false);
  });

  it("says a failed load failed, with a retry, instead of showing an empty catalog", async () => {
    stubApi({ respond: { "/api/procedures": () => json({ error: "boom" }, 500) } });
    renderPage();
    expect(await screen.findByText("Could not load the procedures")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
  });

  it("sends the filters to the server", async () => {
    const api = stubApi();
    renderPage();
    await screen.findByRole("list", { name: "Procedures" });
    await userEvent.selectOptions(screen.getByLabelText("Category"), "Restorative");
    await waitFor(() => expect(api.calls.some((c) => c.url.includes("category=Restorative"))).toBe(true));
    await userEvent.selectOptions(screen.getByLabelText("Show"), "active");
    await waitFor(() => expect(api.calls.some((c) => c.url.includes("status=active"))).toBe(true));
  });

  it("refuses an incomplete new procedure before sending, naming each field", async () => {
    const api = stubApi();
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Add a procedure" }));
    await userEvent.click(addForm().getByRole("button", { name: "Add procedure" }));
    for (const text of ["A code is required.", "A description is required.", "Choose a category.", "Choose where it applies.", "A fee is required (enter 0 for no charge)."]) expect(screen.getByText(text)).toBeInTheDocument();
    expect(posts(api)).toHaveLength(0);
  });

  it("asks an external code set to name its source", async () => {
    stubApi();
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Add a procedure" }));
    expect(addForm().queryByLabelText("Source of the code set")).not.toBeInTheDocument();
    await userEvent.selectOptions(addForm().getByLabelText("Code system"), "External");
    await userEvent.click(addForm().getByRole("button", { name: "Add procedure" }));
    expect(screen.getByText("Name the source of this code set.")).toBeInTheDocument();
  });

  it("asks a CDT code to name its source too, and sends the source with the code", async () => {
    const api = stubApi();
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Add a procedure" }));
    await userEvent.selectOptions(addForm().getByLabelText("Code system"), "CDT");
    await userEvent.type(addForm().getByLabelText("Code"), "d1234");
    await userEvent.type(addForm().getByLabelText("Description"), "Practice wording");
    await userEvent.selectOptions(addForm().getByLabelText("Category"), "Preventive");
    await userEvent.selectOptions(addForm().getByLabelText("Applies to"), "WholeMouth");
    await userEvent.type(addForm().getByLabelText("Fee (US dollars)"), "40");
    await userEvent.click(addForm().getByRole("button", { name: "Add procedure" }));
    expect(addForm().getByText("Name the source of this code set.")).toBeInTheDocument();
    expect(posts(api)).toHaveLength(0);

    await userEvent.type(addForm().getByLabelText("Source of the code set"), "Licensed set held by the practice");
    await userEvent.click(addForm().getByRole("button", { name: "Add procedure" }));
    await waitFor(() => expect(posts(api)[0]?.body).toMatchObject({ codeSystem: "CDT", code: "D1234", sourceName: "Licensed set held by the practice", sourceVersion: null }));
  });

  it("adds a procedure with the fields typed and says it was saved", async () => {
    const api = stubApi();
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Add a procedure" }));
    await userEvent.type(addForm().getByLabelText("Code"), "exam-1");
    await userEvent.type(addForm().getByLabelText("Description"), "Periodic exam");
    await userEvent.selectOptions(addForm().getByLabelText("Category"), "Diagnostic");
    await userEvent.selectOptions(addForm().getByLabelText("Applies to"), "Tooth");
    await userEvent.selectOptions(addForm().getByLabelText("Teeth"), "Permanent");
    await userEvent.type(addForm().getByLabelText("Fee (US dollars)"), "$60");
    await userEvent.click(addForm().getByRole("button", { name: "Add procedure" }));

    await waitFor(() => expect(statusLine()).toHaveTextContent("EXAM-1 added to the catalog."));
    expect(posts(api)[0].body).toMatchObject({ codeSystem: "Local", code: "EXAM-1", description: "Periodic exam", category: "Diagnostic", scope: "Tooth", dentition: "Permanent", fee: 60, sourceName: null });
  });

  it("keeps what was typed and shows the server's message beside the field when it refuses", async () => {
    stubApi({ respond: { "/api/procedures": (b) => (b ? json({ error: "validation_failed", message: "Some fields need attention.", fieldErrors: { code: "A practice code must not look like a CDT code." } }, 400) : json([])) } });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Add a procedure" }));
    await userEvent.type(addForm().getByLabelText("Code"), "D1234");
    await userEvent.type(addForm().getByLabelText("Description"), "Looks like CDT");
    await userEvent.selectOptions(addForm().getByLabelText("Category"), "Diagnostic");
    await userEvent.selectOptions(addForm().getByLabelText("Applies to"), "WholeMouth");
    await userEvent.type(addForm().getByLabelText("Fee (US dollars)"), "10");
    await userEvent.click(addForm().getByRole("button", { name: "Add procedure" }));

    expect(await screen.findByText("A practice code must not look like a CDT code.")).toBeInTheDocument();
    expect(addForm().getByLabelText("Description")).toHaveValue("Looks like CDT");
    expect(statusLine()).toHaveTextContent("Not saved");
  });

  it("changes a fee as a new version: the code is fixed, a reason is required, and the row version travels", async () => {
    const api = stubApi();
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Change procedure CLEAN-1" }));
    const form = screen.getByRole("form", { name: "Change procedure CLEAN-1" });
    expect(within(form).queryByLabelText("Code")).not.toBeInTheDocument();
    expect(form).toHaveTextContent("the code cannot be changed");

    const fee = within(form).getByLabelText("Fee (US dollars)");
    await userEvent.clear(fee);
    await userEvent.type(fee, "105");
    await userEvent.click(within(form).getByRole("button", { name: "Save change" }));
    expect(within(form).getByText("Say why.")).toBeInTheDocument();
    expect(posts(api)).toHaveLength(0);

    await userEvent.type(within(form).getByLabelText("Why is this being changed?"), "Annual fee review");
    await userEvent.click(within(form).getByRole("button", { name: "Save change" }));
    await waitFor(() => expect(statusLine()).toHaveTextContent("CLEAN-1 changed."));
    expect(posts(api)[0].url).toContain("/api/procedures/p1/revise");
    expect(posts(api)[0].body).toMatchObject({ fee: 105, reason: "Annual fee review", rowVersion: "RV1", code: "CLEAN-1" });
  });

  it("shows the shared conflict banner, not a silent overwrite, when someone else changed the procedure", async () => {
    stubApi({ respond: { "/revise": () => json({ error: "concurrency_conflict", message: "Changed by someone else.", entityType: "Procedure", entityId: "p1" }, 409) } });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Change procedure CLEAN-1" }));
    const form = screen.getByRole("form", { name: "Change procedure CLEAN-1" });
    await userEvent.type(within(form).getByLabelText("Why is this being changed?"), "Review");
    await userEvent.click(within(form).getByRole("button", { name: "Save change" }));
    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    expect(screen.getByRole("form", { name: "Change procedure CLEAN-1" })).toBeInTheDocument();       // the open form keeps what was typed
  });

  it("inactivates an unreferenced procedure with a reason and no warning", async () => {
    const api = stubApi();
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Inactivate procedure CLEAN-1" }));
    const form = screen.getByRole("form", { name: "Inactivate procedure CLEAN-1" });
    await within(form).findByLabelText("Why is CLEAN-1 being inactivated?");
    expect(within(form).queryByRole("checkbox")).not.toBeInTheDocument();

    await userEvent.click(within(form).getByRole("button", { name: "Inactivate procedure" }));
    expect(within(form).getByText("Say why.")).toBeInTheDocument();
    await userEvent.type(within(form).getByLabelText("Why is CLEAN-1 being inactivated?"), "No longer offered");
    await userEvent.click(within(form).getByRole("button", { name: "Inactivate procedure" }));
    await waitFor(() => expect(posts(api).at(-1)?.url).toContain("/inactivate"));
    expect(posts(api).at(-1)?.body).toMatchObject({ reason: "No longer offered", acknowledgeUsage: false, rowVersion: "RV1" });
  });

  it("warns how many records refer to a procedure and needs confirmation before inactivating it", async () => {
    const api = stubApi({ usageCount: 3 });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Inactivate procedure CLEAN-1" }));
    const form = screen.getByRole("form", { name: "Inactivate procedure CLEAN-1" });
    expect(await within(form).findByText(/is referred to by 3 records/)).toBeInTheDocument();
    expect(form).toHaveTextContent("does not change or remove those records");

    await userEvent.type(within(form).getByLabelText("Why is CLEAN-1 being inactivated?"), "Retiring");
    await userEvent.click(within(form).getByRole("button", { name: "Inactivate procedure" }));
    expect(within(form).getByText("Confirm that you understand before inactivating.")).toBeInTheDocument();
    expect(posts(api).filter((c) => c.url.includes("/inactivate"))).toHaveLength(0);

    await userEvent.click(within(form).getByRole("checkbox"));
    await userEvent.click(within(form).getByRole("button", { name: "Inactivate procedure" }));
    await waitFor(() => expect(posts(api).at(-1)?.body).toMatchObject({ acknowledgeUsage: true }));
  });

  it("does not offer to inactivate when it could not check where the procedure is used", async () => {
    const api = stubApi({ respond: { "/usage": () => json({ error: "boom" }, 500) } });
    renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Inactivate procedure CLEAN-1" }));
    const form = screen.getByRole("form", { name: "Inactivate procedure CLEAN-1" });
    expect(await within(form).findByText(/cannot be inactivated now/)).toBeInTheDocument();
    expect(within(form).getByRole("button", { name: "Inactivate procedure" })).toBeDisabled();
    expect(posts(api).filter((c) => c.url.includes("/inactivate"))).toHaveLength(0);
  });

  it("offers reactivation, not change, for an inactive procedure", async () => {
    const api = stubApi({ rows: [summary({ isActive: false, status: "Inactive" })] });
    renderPage();
    expect(await screen.findByRole("button", { name: "Reactivate procedure CLEAN-1" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Change procedure CLEAN-1" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Reactivate procedure CLEAN-1" }));
    await userEvent.click(screen.getByRole("button", { name: /^Reactivate procedure$/ }));
    await waitFor(() => expect(posts(api).at(-1)?.url).toContain("/reactivate"));
  });

  it("shows a scheduled fee as not started yet", async () => {
    stubApi({ rows: [summary({ status: "Scheduled", version: version({ effectiveFrom: "2031-01-01" }) })] });
    renderPage();
    expect(await screen.findByText(/not started yet/)).toBeInTheDocument();
  });

  it("loads the version and change history when it is opened", async () => {
    stubApi();
    renderPage();
    await screen.findByRole("list", { name: "Procedures" });
    await userEvent.click(screen.getByText("Fee and change history"));
    const versions = await screen.findByRole("list", { name: "Versions of CLEAN-1" });
    expect(versions).toHaveTextContent("Version 1: $95.50");
    expect(screen.getByRole("list", { name: "Changes to CLEAN-1" })).toHaveTextContent("Created");
  });

  it("has no detectable accessibility violations with the add form open", async () => {
    stubApi();
    const { container } = renderPage();
    await userEvent.click(await screen.findByRole("button", { name: "Add a procedure" }));
    expect(await axe(container)).toHaveNoViolations();
  });
});
