import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "./authApi";
import {
  ALL_PATIENTS_TIMEOUT_MS, downloadAllPatientsPackage, downloadPatientPackage, downloadReadableRecord, exportErrorMessage, fetchExportHistory, fetchExportOverview, wasCancelled,
} from "./patientExportsApi";

// ALV-018-C01: the export client against a small in-memory server that answers like the real API (stable error codes, the headers that say how an export ended; the request and its headers recorded).

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

type Call = { method: string; url: string; init?: RequestInit };
let calls: Call[];
let reply: (call: Call) => Response | Promise<Response> | null;

beforeEach(() => {
  calls = []; reply = () => null;
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    const u = String(url);
    if (u.includes("csrf-token")) return Promise.resolve(json({ token: "t" }));
    const call = { method: init?.method ?? "GET", url: u, init };
    calls.push(call);
    return Promise.resolve(reply(call) ?? json({ error: "not_found", message: "nope" }, 404));
  }));
});
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });

const zip = (headers: Record<string, string> = {}) => new Response(new Blob(["zip"]), {
  status: 200,
  headers: { "Content-Type": "application/zip", "Content-Disposition": "attachment; filename=alveara-patient-package-abc.zip", "X-Export-Id": "e-1", "X-Export-Result": "Complete", "X-Export-Problems": "0", ...headers },
});

describe("downloads", () => {
  it("post to the package route with the CSRF token and the chosen options, and report how the server says it ended", async () => {
    reply = () => zip();

    const file = await downloadPatientPackage("p 1", { includeDocumentFiles: false, folderNaming: "NameAndId" });

    expect(calls[0].url).toBe("/api/patient-exports/patients/p%201/package");
    expect(calls[0].method).toBe("POST");
    expect(new Headers(calls[0].init?.headers).get("X-CSRF-Token")).toBe("t");
    expect(JSON.parse(calls[0].init?.body as string)).toEqual({ includeDocumentFiles: false, folderNaming: "NameAndId" });
    expect(calls[0].init?.credentials).toBe("include");
    expect(file).toMatchObject({ fileName: "alveara-patient-package-abc.zip", exportId: "e-1", result: "Complete", problems: 0 });
  });

  it("leave the folder naming out when none was chosen, so the practice default applies", async () => {
    reply = () => zip();

    await downloadPatientPackage("p1", { includeDocumentFiles: true });

    expect(JSON.parse(calls[0].init?.body as string)).toEqual({ includeDocumentFiles: true });
  });

  it("reach the all-patients route and carry a Partial result and its problem count", async () => {
    reply = () => zip({ "X-Export-Result": "Partial", "X-Export-Problems": "3" });

    const file = await downloadAllPatientsPackage({ includeDocumentFiles: true });

    expect(calls[0].url).toBe("/api/patient-exports/all-patients/package");
    expect(file).toMatchObject({ result: "Partial", problems: 3 });
  });

  it("ask for the readable record with no body, and name the file when the server does not", async () => {
    reply = () => new Response(new Blob(["<html>"]), { status: 200, headers: { "Content-Type": "text/html", "X-Export-Id": "e-2" } });

    const file = await downloadReadableRecord("p1");

    expect(calls[0].url).toBe("/api/patient-exports/patients/p1/readable-record");
    expect(calls[0].init?.body).toBeUndefined();
    expect(file.fileName).toBe("alveara-readable-record.html");
    expect(file.result).toBe("Complete");
  });

  it("throw the server's stable code and message when an export is refused, and a plain code when the body is not JSON", async () => {
    reply = () => json({ error: "export_in_progress", message: "Another export is running." }, 409);
    const refused = await downloadAllPatientsPackage({ includeDocumentFiles: true }).catch((e) => e);
    expect(refused).toBeInstanceOf(ApiError);
    expect(refused).toMatchObject({ status: 409, code: "export_in_progress", message: "Another export is running." });

    reply = () => new Response("<html>bad gateway</html>", { status: 502 });
    const garbled = await downloadPatientPackage("p1", { includeDocumentFiles: true }).catch((e) => e);
    expect(garbled).toMatchObject({ status: 502, code: "unknown_error" });
  });
});

describe("time limit and cancelling", () => {
  // like the browser's fetch: waits for ever, rejects with an AbortError when the signal fires, and at once when it already has
  const hangUntilAborted = (call: Call) => new Promise<Response>((_, reject) => {
    const signal = call.init!.signal as AbortSignal;
    if (signal.aborted) { reject(new DOMException("aborted", "AbortError")); return; }
    signal.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
  });

  it("stop waiting after the time limit and say the export took too long (not that it was cancelled)", async () => {
    vi.useFakeTimers();
    reply = hangUntilAborted;

    const pending = downloadAllPatientsPackage({ includeDocumentFiles: true }).catch((e) => e);
    await vi.advanceTimersByTimeAsync(ALL_PATIENTS_TIMEOUT_MS + 1);

    const error = await pending;
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ code: "export_timeout" });
    expect(wasCancelled(error)).toBe(false);
  });

  it("stop at once when the caller cancels, and report it as a cancellation", async () => {
    reply = hangUntilAborted;
    const cancel = new AbortController();

    const pending = downloadReadableRecord("p1", cancel.signal).catch((e) => e);
    await vi.waitFor(() => expect(calls.length).toBe(1));
    cancel.abort();

    expect(wasCancelled(await pending)).toBe(true);
  });

  it("never start when the caller has already cancelled", async () => {
    reply = hangUntilAborted;
    const cancel = new AbortController();
    cancel.abort();

    const error = await downloadReadableRecord("p1", cancel.signal).catch((e) => e);

    expect(wasCancelled(error)).toBe(true);
  });
});

describe("reads", () => {
  it("read the overview", async () => {
    reply = () => json({ domains: [], excluded: [], maxPatients: 5000 });

    expect((await fetchExportOverview()).maxPatients).toBe(5000);
    expect(calls[0].url).toBe("/api/patient-exports/overview");
  });

  it("read the history a page at a time with the cursor as the last line's time", async () => {
    reply = () => json([]);

    await fetchExportHistory(25);
    await fetchExportHistory(25, "2026-10-26T10:00:00+00:00");

    expect(calls[0].url).toBe("/api/patient-exports/history?limit=25");
    const second = new URL(calls[1].url, "http://x");
    expect(second.searchParams.get("limit")).toBe("25");
    expect(second.searchParams.get("before")).toBe("2026-10-26T10:00:00+00:00");
  });
});

describe("the words for every refusal", () => {
  const codes = ["patient_not_found", "patient_merged", "too_many_patients", "package_too_large", "export_in_progress", "permission_revoked", "permission_policy_unavailable", "invalid_folder_naming", "export_failed", "export_timeout", "csrf_token_invalid"];

  it("has its own plain sentence for each stable code, and none repeats another", () => {
    const messages = codes.map((code) => exportErrorMessage(new ApiError(400, code, "server words")));

    expect(new Set(messages).size).toBe(codes.length);
    expect(messages.every((m) => m.length > 20 && m !== "server words")).toBe(true);
  });

  it("falls back on the status for an unknown code and never shows an unknown error as a crash", () => {
    expect(exportErrorMessage(new ApiError(401, "x", "m"))).toMatch(/Sign in again/);
    expect(exportErrorMessage(new ApiError(403, "permission_denied", "m"))).toMatch(/do not have permission/);
    expect(exportErrorMessage(new ApiError(500, "mystery", "m"))).toMatch(/tell an administrator/);
    expect(exportErrorMessage(new Error("boom"))).toMatch(/tell an administrator/);
  });

  it("says nothing was delivered when an export stops after it started", () => {
    for (const code of ["permission_revoked", "export_failed", "export_timeout", "permission_policy_unavailable"])
      expect(exportErrorMessage(new ApiError(500, code, "m"))).toMatch(/Nothing was delivered|nothing was delivered/);
  });
});
