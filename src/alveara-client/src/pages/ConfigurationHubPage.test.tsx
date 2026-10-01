import { describe, it, expect, vi, afterEach } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { ConfigurationHubPage } from "./ConfigurationHubPage";
import { validateWindows } from "./configuration/availabilityRules";
import type { WindowRow } from "./configuration/availabilityRules";
import { NotificationProvider } from "../components/Notification";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const provider = { id: "p1", staffProfileId: "s1", displayName: "Dr. Rivera", specialty: "Dentistry", isActive: true, rowVersion: "v" };

function stubApi(overrides: Record<string, (init?: RequestInit) => Response> = {}) {
  const fetchMock = vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    for (const [fragment, handler] of Object.entries(overrides)) {
      if (u.includes(fragment)) return Promise.resolve(handler(init));
    }
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "csrf" }));
    if (u.includes("/api/config/practice")) {
      return Promise.resolve(jsonResponse({ id: null, name: null, phone: null, addressLine: null, timeZoneId: "America/Chicago", currency: "USD", configured: false, rowVersion: null }));
    }
    if (u.includes("/api/config/locations")) return Promise.resolve(jsonResponse([]));
    if (u.includes("/api/config/providers/p1/availability")) return Promise.resolve(jsonResponse({ revision: 3, windows: [{ dayOfWeek: 2, startLocal: "09:00", endLocal: "17:00" }] }));
    if (u.includes("/api/config/providers/p1/blocked-time")) return Promise.resolve(jsonResponse([]));
    if (u.includes("/api/config/providers")) return Promise.resolve(jsonResponse([provider]));
    if (u.includes("/api/config/scheduling")) {
      return Promise.resolve(
        jsonResponse({
          timeZoneId: "America/Chicago", activeLocationId: "l1", activeLocationName: "Main Office",
          providers: [{ providerId: "p1", displayName: "Dr. Rivera", specialty: "Dentistry", weeklyAvailability: [{ dayOfWeek: 2, startLocal: "09:00", endLocal: "17:00" }] }],
          operatories: [{ id: "o1", name: "Op 1" }],
          appointmentTypes: [{ id: "t1", name: "Exam", defaultDurationMinutes: 30 }],
        })
      );
    }
    return Promise.resolve(jsonResponse([]));
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function renderHub() {
  return render(
    <NotificationProvider>
      <ConfigurationHubPage />
    </NotificationProvider>
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("ConfigurationHubPage (ALV-N003)", () => {
  it("shows every configuration section as a tab and starts on practice information", async () => {
    stubApi();
    renderHub();
    for (const name of ["Practice", "Staff", "Providers", "Operatories", "Appointment types", "Availability", "Scheduling preview"]) {
      expect(screen.getByRole("tab", { name })).toBeInTheDocument();
    }
    expect(screen.getByRole("tab", { name: "Practice" })).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByLabelText("Practice name")).toBeInTheDocument();
    // Time zone and currency are reflected read-only from the deployment, never editable here.
    expect(screen.getByLabelText("Time zone")).toHaveValue("America/Chicago");
    expect(screen.getByLabelText("Time zone")).toHaveAttribute("readonly");
    expect(screen.getByLabelText("Currency")).toHaveValue("USD");
  });

  it("surfaces permission-denied rather than a blank or generic error when the server returns 403", async () => {
    stubApi({ "/api/config/practice": () => jsonResponse({ error: "permission_denied", required: "ManagePracticeConfiguration" }, 403) });
    renderHub();
    expect(await screen.findAllByText(/don't have permission/i)).not.toHaveLength(0);
  });

  it("asks before discarding unsaved practice edits when switching tabs, and stays put if declined", async () => {
    stubApi();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    renderHub();
    await userEvent.type(await screen.findByLabelText("Practice name"), "Alveara");
    await waitFor(() => expect(screen.getByText("Unsaved changes")).toBeInTheDocument());

    await userEvent.click(screen.getByRole("tab", { name: "Staff" }));
    expect(confirm).toHaveBeenCalled();
    expect(screen.getByRole("tab", { name: "Practice" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByLabelText("Practice name")).toHaveValue("Alveara");

    confirm.mockReturnValue(true);
    await userEvent.click(screen.getByRole("tab", { name: "Staff" }));
    expect(screen.getByRole("tab", { name: "Staff" })).toHaveAttribute("aria-selected", "true");
  });

  it("requires a practice name before saving and sends the version it read", async () => {
    const fetchMock = stubApi({
      "/api/config/practice": (init) =>
        init?.method === "PUT"
          ? jsonResponse({ id: "x", name: "Alveara", phone: null, addressLine: null, timeZoneId: "America/Chicago", currency: "USD", configured: true, rowVersion: "v2" })
          : jsonResponse({ id: null, name: null, phone: null, addressLine: null, timeZoneId: "America/Chicago", currency: "USD", configured: false, rowVersion: null }),
    });
    renderHub();
    const save = await screen.findByRole("button", { name: "Save practice information" });
    expect(save).toBeDisabled(); // nothing to save yet

    await userEvent.type(screen.getByLabelText("Phone"), "555");
    await userEvent.click(save);
    expect(await screen.findByText("Practice name is required.")).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([, init]) => (init as RequestInit | undefined)?.method === "PUT")).toBe(false);

    await userEvent.type(screen.getByLabelText("Practice name"), "Alveara");
    await userEvent.click(save);
    await waitFor(() => {
      const put = fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === "PUT");
      expect(put).toBeDefined();
      expect(JSON.parse((put![1] as RequestInit).body as string)).toMatchObject({ name: "Alveara", phone: "555", rowVersion: null });
    });
  });

  it("disables the practice form while a save is in flight, so its response cannot overwrite newer typing", async () => {
    let finish!: (r: Response) => void;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
        const u = String(url);
        if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "csrf" }));
        if (u.includes("/api/config/practice")) {
          if (init?.method === "PUT") return new Promise<Response>((resolve) => (finish = resolve));
          return Promise.resolve(jsonResponse({ id: null, name: null, phone: null, addressLine: null, timeZoneId: "America/Chicago", currency: "USD", configured: false, rowVersion: null }));
        }
        return Promise.resolve(jsonResponse([]));
      })
    );
    renderHub();
    await userEvent.type(await screen.findByLabelText("Practice name"), "Alveara");
    await userEvent.click(screen.getByRole("button", { name: "Save practice information" }));

    await waitFor(() => expect(screen.getByLabelText("Practice name")).toBeDisabled());
    expect(screen.getByLabelText("Phone")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Save practice information" })).toBeDisabled();

    await act(async () =>
      finish(jsonResponse({ id: "x", name: "Alveara", phone: null, addressLine: null, timeZoneId: "America/Chicago", currency: "USD", configured: true, rowVersion: "v2" }))
    );
    await waitFor(() => expect(screen.getByLabelText("Practice name")).toBeEnabled());
    expect(screen.getByLabelText("Practice name")).toHaveValue("Alveara");
  });

  it("previews exactly what scheduling will be offered, from the scheduling read model", async () => {
    stubApi();
    renderHub();
    await userEvent.click(screen.getByRole("tab", { name: "Scheduling preview" }));
    expect(await screen.findByText(/Active location: Main Office/)).toBeInTheDocument();
    expect(screen.getByText(/Dr\. Rivera/)).toBeInTheDocument();
    expect(screen.getByText(/Tuesday 09:00-17:00/)).toBeInTheDocument();
    expect(screen.getByText("Op 1")).toBeInTheDocument();
    expect(screen.getByText("Exam - 30 min")).toBeInTheDocument();
  });

  it("states plainly when nothing is schedulable yet instead of showing an empty table", async () => {
    stubApi({ "/api/config/scheduling": () => jsonResponse({ timeZoneId: "America/Chicago", activeLocationId: null, activeLocationName: null, providers: [], operatories: [], appointmentTypes: [] }) });
    renderHub();
    await userEvent.click(screen.getByRole("tab", { name: "Scheduling preview" }));
    expect(await screen.findByText("Nothing is schedulable yet")).toBeInTheDocument();
  });
});

describe("Accessibility (ALV-N003)", () => {
  it("has no detectable axe violations on the practice tab, a list with an open edit form, and the availability editor", async () => {
    stubApi({
      "/api/config/operatories": () => jsonResponse([{ id: "o1", locationId: "l1", name: "Op 1", isActive: true, rowVersion: "v" }]),
    });
    const { container } = renderHub();
    await screen.findByLabelText("Practice name");
    expect(await axe(container)).toHaveNoViolations();

    await userEvent.click(screen.getByRole("tab", { name: "Operatories" }));
    await userEvent.click(await screen.findByRole("button", { name: "Edit Op 1" }));
    expect(await screen.findByRole("form", { name: "Edit operatory" })).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();

    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    await userEvent.click(screen.getByRole("tab", { name: "Availability" }));
    await userEvent.selectOptions(await screen.findByLabelText("Provider"), "p1");
    await screen.findByRole("group", { name: "Window 1" });
    expect(await axe(container)).toHaveNoViolations();
  });
});

describe("Availability tab", () => {
  async function openAvailability() {
    renderHub();
    await userEvent.click(screen.getByRole("tab", { name: "Availability" }));
    await userEvent.selectOptions(await screen.findByLabelText("Provider"), "p1");
    expect(await screen.findByRole("group", { name: "Window 1" })).toBeInTheDocument();
  }

  it("loads a provider's weekly hours and saves an edited schedule atomically", async () => {
    const fetchMock = stubApi({
      "/api/config/providers/p1/availability": (init) =>
        init?.method === "PUT"
          ? jsonResponse({ revision: 4, windows: [{ dayOfWeek: 2, startLocal: "08:00", endLocal: "17:00" }] })
          : jsonResponse({ revision: 3, windows: [{ dayOfWeek: 2, startLocal: "09:00", endLocal: "17:00" }] }),
    });
    await openAvailability();
    const save = screen.getByRole("button", { name: "Save weekly hours" });
    expect(save).toBeDisabled(); // unchanged

    const start = screen.getByLabelText("Start time for window 1") as HTMLInputElement;
    await userEvent.clear(start);
    await userEvent.type(start, "08:00");
    expect(await screen.findByText("Unsaved changes")).toBeInTheDocument();
    await userEvent.click(save);

    await waitFor(() => {
      const put = fetchMock.mock.calls.find(([u, init]) => String(u).includes("/availability") && (init as RequestInit | undefined)?.method === "PUT");
      expect(put).toBeDefined();
      // The revision the schedule was read at travels with the replacement (R02: versioned aggregate).
      expect(JSON.parse((put![1] as RequestInit).body as string)).toEqual({ windows: [{ dayOfWeek: 2, startLocal: "08:00", endLocal: "17:00" }], revision: 3 });
    });
    await waitFor(() => expect(screen.queryByText("Unsaved changes")).not.toBeInTheDocument());
  });

  it("refuses to save a window that ends before it starts or overlaps another, without calling the server", async () => {
    const fetchMock = stubApi();
    await openAvailability();
    await userEvent.click(screen.getByRole("button", { name: "Add window" }));
    // New window defaults to Monday 09:00-17:00; make it Tuesday and overlapping the existing one.
    await userEvent.selectOptions(screen.getAllByRole("combobox", { name: /Day for window 2/ })[0], "2");
    await userEvent.click(screen.getByRole("button", { name: "Save weekly hours" }));
    expect(await screen.findByText(/Overlaps another Tuesday window/)).toBeInTheDocument();

    const end = screen.getByLabelText("End time for window 2") as HTMLInputElement;
    await userEvent.clear(end);
    await userEvent.type(end, "08:00");
    await userEvent.click(screen.getByRole("button", { name: "Save weekly hours" }));
    expect(await screen.findByText("A window must end after it starts.")).toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([, init]) => (init as RequestInit | undefined)?.method === "PUT")).toBe(false);
  });

  it("shows the server's reason when blocked time lands in a daylight-saving gap", async () => {
    stubApi({
      "/api/config/providers/p1/blocked-time": (init) =>
        init?.method === "POST"
          ? jsonResponse({ error: "invalid_local_time", message: "2026-03-08 02:30 does not exist in America/Chicago (DST spring-forward gap). Ask the user to pick a valid time." }, 400)
          : jsonResponse([]),
    });
    await openAvailability();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText("Blocked from"), "2026-03-08T02:30");
    await user.type(screen.getByLabelText("Blocked until"), "2026-03-08T04:00");
    await user.click(screen.getByRole("button", { name: "Add blocked time" }));
    expect(await screen.findByText(/does not exist in America\/Chicago/)).toBeInTheDocument();
  });

  it("explains how to proceed when there are no providers yet", async () => {
    stubApi({ "/api/config/providers": () => jsonResponse([]) });
    renderHub();
    await userEvent.click(screen.getByRole("tab", { name: "Availability" }));
    expect(await screen.findByText("No active providers yet")).toBeInTheDocument();
  });
});

describe("Availability tab provider binding and revisions (ALV-N003 R02)", () => {
  const p2 = { id: "p2", staffProfileId: "s2", displayName: "Dr. Chen", specialty: "Hygiene", isActive: true, rowVersion: "v" };

  /** Per-provider availability responses the test completes by hand, so ordering is fully deterministic. */
  function deferredApi() {
    const pending: Record<string, ((r: Response) => void)[]> = {};
    const requests: { url: string; init?: RequestInit }[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
        const u = String(url);
        requests.push({ url: u, init });
        if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "csrf" }));
        const availability = u.match(/providers\/(p\d)\/availability/);
        if (availability && init?.method !== "PUT") {
          return new Promise<Response>((resolve) => {
            (pending[availability[1]] ??= []).push(resolve);
          });
        }
        if (availability && init?.method === "PUT") {
          const body = JSON.parse(init.body as string);
          return Promise.resolve(jsonResponse({ revision: body.revision + 1, windows: body.windows }));
        }
        if (/providers\/p\d\/blocked-time/.test(u)) return Promise.resolve(jsonResponse([]));
        if (u.includes("/api/config/providers")) return Promise.resolve(jsonResponse([provider, p2]));
        return Promise.resolve(jsonResponse([]));
      })
    );
    const complete = (id: string, revision: number, start: string, end = "17:00") =>
      act(async () => {
        pending[id].shift()!(jsonResponse({ revision, windows: [{ dayOfWeek: 1, startLocal: start, endLocal: end }] }));
      });
    return { complete, requests };
  }

  async function openAvailability() {
    renderHub();
    await userEvent.click(screen.getByRole("tab", { name: "Availability" }));
    return screen.findByLabelText("Provider");
  }

  it("discards a delayed response for a provider that is no longer selected, and never shows or saves its rows under the new one", async () => {
    const { complete, requests } = deferredApi();
    const picker = await openAvailability();

    await userEvent.selectOptions(picker, "p1"); // A: response left pending
    await userEvent.selectOptions(picker, "p2"); // B selected meanwhile
    await complete("p2", 7, "13:00");            // B's response arrives first
    expect(await screen.findByLabelText("Start time for window 1")).toHaveValue("13:00");

    await complete("p1", 2, "09:00");            // A's older response arrives last
    expect(screen.getByLabelText("Provider")).toHaveValue("p2");
    expect(screen.getByLabelText("Start time for window 1")).toHaveValue("13:00"); // still B's hours, not A's 09:00

    // Editing and saving submits B's rows to B at B's revision - never A's.
    await userEvent.clear(screen.getByLabelText("End time for window 1"));
    await userEvent.type(screen.getByLabelText("End time for window 1"), "18:00");
    await userEvent.click(screen.getByRole("button", { name: "Save weekly hours" }));
    await waitFor(() => expect(requests.some((r) => r.init?.method === "PUT")).toBe(true));
    const put = requests.find((r) => r.init?.method === "PUT")!;
    expect(put.url).toContain("/providers/p2/availability");
    expect(JSON.parse(put.init!.body as string)).toEqual({ windows: [{ dayOfWeek: 1, startLocal: "13:00", endLocal: "18:00" }], revision: 7 });
  });

  it("hides the previous provider's editor immediately while the newly selected provider loads", async () => {
    const { complete } = deferredApi();
    const picker = await openAvailability();
    await userEvent.selectOptions(picker, "p1");
    await complete("p1", 1, "09:00");
    expect(await screen.findByRole("group", { name: "Window 1" })).toBeInTheDocument();

    await userEvent.selectOptions(picker, "p2");
    // p2 still pending: nothing of p1's schedule may remain visible or savable.
    expect(screen.queryByRole("group", { name: "Window 1" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save weekly hours" })).not.toBeInTheDocument();
    expect(screen.getByText("Loading schedule…")).toBeInTheDocument();
  });

  it("still asks before discarding a dirty schedule when switching providers", async () => {
    const { complete } = deferredApi();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    const picker = await openAvailability();
    await userEvent.selectOptions(picker, "p1");
    await complete("p1", 1, "09:00");
    await userEvent.clear(await screen.findByLabelText("Start time for window 1"));
    await userEvent.type(screen.getByLabelText("Start time for window 1"), "08:00");

    await userEvent.selectOptions(picker, "p2");
    expect(confirm).toHaveBeenCalled();
    expect(screen.getByLabelText("Provider")).toHaveValue("p1");
    expect(screen.getByLabelText("Start time for window 1")).toHaveValue("08:00");
  });

  const conflictBody = { error: "concurrency_conflict", entityType: "ProviderAvailability", entityId: "p1" };

  it("presents a stale schedule save with the shared conflict banner, keeps the draft if the reload fails, and recovers on retry", async () => {
    let reads = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
        const u = String(url);
        if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "csrf" }));
        if (u.includes("/providers/p1/availability")) {
          if (init?.method === "PUT") return Promise.resolve(jsonResponse(conflictBody, 409));
          reads += 1;
          if (reads === 1) return Promise.resolve(jsonResponse({ revision: 1, windows: [{ dayOfWeek: 1, startLocal: "09:00", endLocal: "17:00" }] }));
          if (reads === 2) return Promise.reject(new Error("network down"));
          return Promise.resolve(jsonResponse({ revision: 2, windows: [{ dayOfWeek: 1, startLocal: "10:00", endLocal: "16:00" }] }));
        }
        if (u.includes("/providers/p1/blocked-time")) return Promise.resolve(jsonResponse([]));
        if (u.includes("/api/config/providers")) return Promise.resolve(jsonResponse([provider]));
        return Promise.resolve(jsonResponse([]));
      })
    );
    renderHub();
    await userEvent.click(screen.getByRole("tab", { name: "Availability" }));
    await userEvent.selectOptions(await screen.findByLabelText("Provider"), "p1");
    await userEvent.clear(await screen.findByLabelText("Start time for window 1"));
    await userEvent.type(screen.getByLabelText("Start time for window 1"), "08:00");
    await userEvent.click(screen.getByRole("button", { name: "Save weekly hours" }));

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Reload current version" })); // reload fails
    expect(await screen.findByText(/Could not reload|network down/)).toBeInTheDocument();
    expect(screen.getByLabelText("Start time for window 1")).toHaveValue("08:00");          // draft intact
    expect(screen.getByText("Someone else changed this while you were editing")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Reload current version" })); // retry succeeds
    await waitFor(() => expect(screen.getByLabelText("Start time for window 1")).toHaveValue("10:00"));
    expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument();
  });
});

describe("Availability editing-session completion protection (ALV-N003 R03)", () => {
  const p2 = { id: "p2", staffProfileId: "s2", displayName: "Dr. Chen", specialty: "Hygiene", isActive: true, rowVersion: "v" };
  const monday = (start: string, end = "17:00") => ({ dayOfWeek: 1, startLocal: start, endLocal: end });
  const block = { id: "b1", startUtc: "2026-05-04T14:00:00Z", endUtc: "2026-05-04T15:00:00Z", startLocal: "2026-05-04T09:00", endLocal: "2026-05-04T10:00", reason: "Meeting" };
  type Windows = ReturnType<typeof monday>[];

  /**
   * Fully hand-driven API: GET availability answers from a per-provider queue (the last answer repeats);
   * the first `hold.*` writes of each kind are held until the test completes them, so a response can be
   * delivered in a different editing session than the one that issued it.
   */
  function drivenApi(availability: Record<string, Windows[]>, revisions: Record<string, number[]>, blocked: (typeof block)[] = []) {
    const held = { put: [] as ((r: Response) => void)[], post: [] as ((r: Response) => void)[], del: [] as ((r: Response) => void)[] };
    const hold = { put: 0, post: 0, del: 0 };
    const puts: { url: string; body: { windows: unknown[]; revision: number } }[] = [];
    const reads: Record<string, number> = {};
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
        const u = String(url);
        if (u.includes("/api/auth/csrf-token")) return Promise.resolve(jsonResponse({ token: "csrf" }));
        const avail = u.match(/providers\/(p\d)\/availability/);
        if (avail) {
          const id = avail[1];
          if (init?.method === "PUT") {
            const body = JSON.parse(init.body as string);
            puts.push({ url: u, body });
            if (hold.put > 0) {
              hold.put -= 1;
              return new Promise<Response>((resolve) => held.put.push(resolve));
            }
            return Promise.resolve(jsonResponse({ revision: body.revision + 1, windows: body.windows }));
          }
          const n = (reads[id] = (reads[id] ?? 0) + 1);
          const windows = availability[id][Math.min(n, availability[id].length) - 1];
          const revision = revisions[id][Math.min(n, revisions[id].length) - 1];
          return Promise.resolve(jsonResponse({ revision, windows }));
        }
        if (/providers\/p\d\/blocked-time/.test(u)) {
          if (init?.method === "POST") {
            if (hold.post > 0) {
              hold.post -= 1;
              return new Promise<Response>((resolve) => held.post.push(resolve));
            }
            return Promise.resolve(jsonResponse(block, 201));
          }
          if (init?.method === "DELETE") {
            if (hold.del > 0) {
              hold.del -= 1;
              return new Promise<Response>((resolve) => held.del.push(resolve));
            }
            return Promise.resolve(new Response(null, { status: 204 }));
          }
          return Promise.resolve(jsonResponse(blocked));
        }
        if (u.includes("/api/config/providers")) return Promise.resolve(jsonResponse([provider, p2]));
        return Promise.resolve(jsonResponse([]));
      })
    );
    return { held, hold, puts };
  }

  async function select(id: string) {
    await userEvent.selectOptions(screen.getByLabelText("Provider"), id);
  }

  async function openTab() {
    renderHub();
    await userEvent.click(screen.getByRole("tab", { name: "Availability" }));
    await screen.findByLabelText("Provider");
  }

  /** A-save (held) -> B -> A (fresh read, revision 2) and returns the reopened A editor's start input. */
  async function abandonSaveAndReopenSameProvider(api: ReturnType<typeof drivenApi>) {
    api.hold.put = 1;
    await openTab();
    await select("p1");
    const start = await screen.findByLabelText("Start time for window 1");
    await userEvent.clear(start);
    await userEvent.type(start, "08:00");
    await userEvent.click(screen.getByRole("button", { name: "Save weekly hours" })); // response held; server has "committed" revision 2
    await select("p2");
    await screen.findByDisplayValue("13:00");
    await select("p1");
    await waitFor(() => expect(screen.getByLabelText("Start time for window 1")).toHaveValue("08:00"));
    return screen.getByLabelText("Start time for window 1");
  }

  it("ignores a delayed save response from an abandoned session when the same provider is reopened and edited again (A-save -> B -> A -> new draft -> old response)", async () => {
    const api = drivenApi({ p1: [[monday("09:00")], [monday("08:00")]], p2: [[monday("13:00")]] }, { p1: [1, 2], p2: [5] });
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const fresh = await abandonSaveAndReopenSameProvider(api);
    await userEvent.clear(fresh);
    await userEvent.type(fresh, "10:00"); // a NEW unsaved draft
    expect(screen.getByText("Unsaved changes")).toBeInTheDocument();

    await act(async () => api.held.put[0](jsonResponse({ revision: 2, windows: [monday("08:00")] }))); // the abandoned session's response lands

    expect(screen.getByLabelText("Start time for window 1")).toHaveValue("10:00"); // draft not overwritten
    expect(screen.getByText("Unsaved changes")).toBeInTheDocument(); // still dirty
    expect(screen.queryByText("Weekly availability saved.")).not.toBeInTheDocument(); // no success notice from the dead session

    await userEvent.click(screen.getByRole("button", { name: "Save weekly hours" })); // still saveable, at the revision this session loaded
    await waitFor(() => expect(api.puts).toHaveLength(2));
    expect(api.puts[1].body).toEqual({ windows: [monday("10:00")], revision: 2 });
  });

  it("ignores a delayed save ERROR from an abandoned session too", async () => {
    const api = drivenApi({ p1: [[monday("09:00")], [monday("08:00")]], p2: [[monday("13:00")]] }, { p1: [1, 2], p2: [5] });
    vi.spyOn(window, "confirm").mockReturnValue(true);
    await abandonSaveAndReopenSameProvider(api);

    await act(async () => api.held.put[0](jsonResponse({ error: "concurrency_conflict", entityType: "ProviderAvailability", entityId: "p1" }, 409)));

    expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument();
  });

  it("keeps edits typed while a save is in flight, and advances the baseline and revision so they remain dirty and saveable", async () => {
    const api = drivenApi({ p1: [[monday("09:00")]] }, { p1: [1] });
    api.hold.put = 1;
    await openTab();
    await select("p1");
    const start = await screen.findByLabelText("Start time for window 1");
    await userEvent.clear(start);
    await userEvent.type(start, "08:00");
    await userEvent.click(screen.getByRole("button", { name: "Save weekly hours" })); // in flight, snapshot = 08:00-17:00
    expect(screen.getByRole("button", { name: "Saving…" })).toBeDisabled(); // no second concurrent save

    const end = screen.getByLabelText("End time for window 1");
    await userEvent.clear(end);
    await userEvent.type(end, "16:00"); // typed after the snapshot

    await act(async () => api.held.put[0](jsonResponse({ revision: 2, windows: [monday("08:00", "17:00")] })));

    expect(screen.getByLabelText("End time for window 1")).toHaveValue("16:00"); // not clobbered by the response
    expect(screen.getByText("Unsaved changes")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Save weekly hours" }));
    await waitFor(() => expect(api.puts).toHaveLength(2));
    expect(api.puts[1].body).toEqual({ windows: [monday("08:00", "16:00")], revision: 2 });
  });

  async function startBlockDraft() {
    await userEvent.type(screen.getByLabelText("Blocked from"), "2026-05-04T09:00");
    await userEvent.type(screen.getByLabelText("Blocked until"), "2026-05-04T10:00");
    await userEvent.type(screen.getByLabelText("Reason (optional)"), "Meeting");
  }

  async function abandonBlockedAddAndReopenSameProvider(api: ReturnType<typeof drivenApi>) {
    api.hold.post = 1;
    await openTab();
    await select("p1");
    await screen.findByLabelText("Blocked from");
    await startBlockDraft();
    await userEvent.click(screen.getByRole("button", { name: "Add blocked time" })); // POST held
    await select("p2");
    await screen.findByDisplayValue("13:00");
    await select("p1");
    await screen.findByLabelText("Blocked from");
  }

  it("a delayed blocked-time ADD from an abandoned session does not wipe the new draft or announce success", async () => {
    const api = drivenApi({ p1: [[monday("09:00")]], p2: [[monday("13:00")]] }, { p1: [1], p2: [5] });
    vi.spyOn(window, "confirm").mockReturnValue(true);
    await abandonBlockedAddAndReopenSameProvider(api);
    await userEvent.type(screen.getByLabelText("Reason (optional)"), "new draft");

    await act(async () => api.held.post[0](jsonResponse(block, 201)));

    expect(screen.getByLabelText("Reason (optional)")).toHaveValue("new draft");
    expect(screen.queryByText("Blocked time added.")).not.toBeInTheDocument();
  });

  it("a delayed blocked-time ADD error from an abandoned session is not shown in the new session", async () => {
    const api = drivenApi({ p1: [[monday("09:00")]], p2: [[monday("13:00")]] }, { p1: [1], p2: [5] });
    vi.spyOn(window, "confirm").mockReturnValue(true);
    await abandonBlockedAddAndReopenSameProvider(api);

    await act(async () => api.held.post[0](jsonResponse({ error: "invalid_local_time", message: "Stale session error" }, 400)));

    expect(screen.queryByText("Stale session error")).not.toBeInTheDocument();
  });

  it("a delayed blocked-time REMOVE from an abandoned session does not notify or replace the fresh list", async () => {
    const api = drivenApi({ p1: [[monday("09:00")]], p2: [[monday("13:00")]] }, { p1: [1], p2: [5] }, [block]);
    vi.spyOn(window, "confirm").mockReturnValue(true);
    api.hold.del = 1;
    await openTab();
    await select("p1");
    await userEvent.click(await screen.findByRole("button", { name: /Remove blocked time starting/ })); // DELETE held
    await select("p2");
    await screen.findByDisplayValue("13:00");
    await select("p1");
    await screen.findByText("Meeting");

    await act(async () => api.held.del[0](new Response(null, { status: 204 })));

    expect(screen.queryByText("Blocked time removed.")).not.toBeInTheDocument();
    expect(screen.getByText("Meeting")).toBeInTheDocument(); // the new session's own (fresh) list is untouched
  });
});

describe("validateWindows", () => {
  const w = (dayOfWeek: number, startLocal: string, endLocal: string, key = 1): WindowRow => ({ key, dayOfWeek: String(dayOfWeek), startLocal, endLocal });

  it("accepts adjacent windows on the same day and the same hours on different days", () => {
    expect(validateWindows([w(1, "08:00", "12:00"), w(1, "12:00", "17:00", 2), w(2, "08:00", "12:00", 3)])).toEqual({});
  });

  it("flags empty, inverted, and overlapping windows by row", () => {
    const errors = validateWindows([w(1, "", "12:00"), w(1, "10:00", "09:00", 2), w(3, "08:00", "12:00", 3), w(3, "11:00", "15:00", 4)]);
    expect(errors[0]).toMatch(/both a start and an end/);
    expect(errors[1]).toMatch(/end after it starts/);
    expect(errors[3]).toMatch(/Overlaps another Wednesday window/);
    expect(errors[2]).toBeUndefined();
  });
});
