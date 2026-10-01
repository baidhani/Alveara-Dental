import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
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
    if (u.includes("/api/config/providers/p1/availability")) return Promise.resolve(jsonResponse([{ dayOfWeek: 2, startLocal: "09:00", endLocal: "17:00" }]));
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
        init?.method === "PUT" ? jsonResponse([{ dayOfWeek: 2, startLocal: "08:00", endLocal: "17:00" }]) : jsonResponse([{ dayOfWeek: 2, startLocal: "09:00", endLocal: "17:00" }]),
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
      expect(JSON.parse((put![1] as RequestInit).body as string)).toEqual({ windows: [{ dayOfWeek: 2, startLocal: "08:00", endLocal: "17:00" }] });
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
