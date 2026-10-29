import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "./authApi";
import { getAppointments, getFollowUpTasks, getSchedule, reportErrorMessage, wasCancelled } from "./reportsApi";

// ALV-N006 slice 6a: how the client asks for a report and how it says a refusal.

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
let urls: string[];

beforeEach(() => { urls = []; vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => { urls.push(String(url)); return Promise.resolve(json({})); })); });
afterEach(() => { vi.unstubAllGlobals(); });

describe("asking for a report", () => {
  it("sends the range, each chosen provider as its own parameter, and the location only when one is chosen", async () => {
    await getSchedule({ from: "2030-01-14", to: "2030-01-16", providerIds: ["pa", "pb"], locationId: "loc1" });
    await getSchedule({ from: "2030-01-14", to: "2030-01-16", providerIds: [], locationId: null });

    const first = new URL(urls[0], "http://x");
    expect(first.pathname).toBe("/api/reports/schedule");
    expect([first.searchParams.get("from"), first.searchParams.get("to"), first.searchParams.getAll("providerIds"), first.searchParams.get("locationId")]).toEqual(["2030-01-14", "2030-01-16", ["pa", "pb"], "loc1"]);
    expect(urls[1]).toBe("/api/reports/schedule?from=2030-01-14&to=2030-01-16");
  });

  it("sends only the criteria that were given, and the page position as two parameters", async () => {
    const query = { from: "2030-01-14", to: "2030-01-16", providerIds: [], locationId: null };
    await getAppointments(query, { route: "cancellations/appointments", measure: "Cancelled", leadTime: "LessThan24h", after: { afterStart: "2030-01-14T15:00:00+00:00", afterId: "a2" } });
    await getFollowUpTasks(query, { kind: "Manual", outcome: "OpenOverdue", asOf: "2030-01-15T18:00:00+00:00" });

    const a = new URL(urls[0], "http://x");
    expect(a.pathname).toBe("/api/reports/cancellations/appointments");
    expect([a.searchParams.get("measure"), a.searchParams.get("leadTime"), a.searchParams.get("afterId"), a.searchParams.has("status"), a.searchParams.has("providerId")]).toEqual(["Cancelled", "LessThan24h", "a2", false, false]);
    const f = new URL(urls[1], "http://x");
    expect([f.pathname, f.searchParams.get("kind"), f.searchParams.get("outcome"), f.searchParams.get("asOf"), f.searchParams.has("afterId")]).toEqual(["/api/reports/follow-ups/tasks", "Manual", "OpenOverdue", "2030-01-15T18:00:00+00:00", false]);
  });
});

describe("saying a refusal", () => {
  it("turns each known code into words and never shows a code", () => {
    for (const code of ["dates_required", "invalid_date", "invalid_range", "range_too_large", "too_many_providers", "unknown_provider", "unknown_location", "filter_not_supported", "report_too_large", "audit_unavailable"]) {
      const text = reportErrorMessage(new ApiError(400, code, "Server words."));
      expect(text).not.toContain("_");
      expect(text.length).toBeGreaterThan(20);
    }
    expect(reportErrorMessage(new ApiError(400, "range_too_large", "x"))).toBe("That range is longer than a report can cover. Choose a shorter one.");
  });

  it("says a forbidden call, a lost connection, an unknown code and something that is not an error each in their own words", () => {
    expect(reportErrorMessage(new ApiError(403, "forbidden", "x"))).toBe("You do not have access to this report.");
    expect(reportErrorMessage(new ApiError(0, "network", "x"))).toBe("The server could not be reached. Check your connection and try again.");
    expect(reportErrorMessage(new ApiError(500, "something_new", "The server's own words."))).toBe("The server's own words.");
    expect(reportErrorMessage("boom")).toBe("The report could not be produced.");
  });

  it("tells a cancelled request from a failed one", () => {
    expect(wasCancelled(new DOMException("x", "AbortError"))).toBe(true);
    expect(wasCancelled(new ApiError(500, "x", "x"))).toBe(false);
  });
});
