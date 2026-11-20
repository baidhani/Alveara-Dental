import { describe, it, expect, vi, afterEach } from "vitest";
import { recordPrint } from "./patientsApi";

/** ALV-N025 (OQ-1): an in-application Print button records the request first - and a failure to record it never stops the printing. */
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("recordPrint", () => {
  it("posts the material type and the resource id with the CSRF token", async () => {
    const calls: { url: string; init?: RequestInit }[] = [];
    vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
      if (String(url).includes("csrf-token")) return json(200, { token: "csrf-1" });
      calls.push({ url: String(url), init });
      return new Response(null, { status: 204 });
    }));
    await recordPrint("p-1", "Statement", "doc-9");
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toBe("/api/patients/p-1/print-events");
    expect(calls[0].init?.method).toBe("POST");
    expect(JSON.parse(String(calls[0].init?.body))).toEqual({ materialType: "Statement", resourceId: "doc-9" });
  });

  it("never throws when the record cannot be written, so printing goes ahead", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => { throw new TypeError("network down"); }));
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    await expect(recordPrint("p-1", "Receipt", "doc-1")).resolves.toBeUndefined();
    expect(warn).toHaveBeenCalledWith("print_event_failed", expect.objectContaining({ materialType: "Receipt" }));
  });
});
