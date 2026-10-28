import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "./authApi";
import { downloadErrorReport, importErrorMessage, listRows, problemsOf, uploadBatch, wasCancelled } from "./bulkImportApi";

// ALV-018-C02 slice 7: the bulk import client against a small in-memory server that answers like the real API (stable error codes; the request, its headers and its body recorded).

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

type Call = { method: string; url: string; init?: RequestInit };
let calls: Call[];
let reply: (call: Call) => Response | null;

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
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("uploading a file", () => {
  it("posts the CSV itself as the body, with the kind and label in the query and the CSRF token", async () => {
    reply = () => json({ batch: { id: "b1" }, created: true }, 201);
    const file = new File(["a,b\r\n1,2\r\n"], "x.csv", { type: "text/csv" });

    const result = await uploadBatch("OpeningBalances", "Old system, Oct", file);

    expect(result.batch.id).toBe("b1");
    expect(calls[0].method).toBe("POST");
    expect(calls[0].url).toBe("/api/import-batches?sourceKind=OpeningBalances&label=Old+system%2C+Oct");
    expect(new Headers(calls[0].init?.headers).get("X-CSRF-Token")).toBe("t");
    expect(new Headers(calls[0].init?.headers).get("Content-Type")).toBe("text/csv");
    expect(calls[0].init?.body).toBe(file);
  });

  it("surfaces the problems of a refused file, without a value from the file", async () => {
    reply = () => json({ error: "import_file_rejected", message: "no", problems: [{ code: "bad_cell", row: 5, column: "DOB", message: "Not a date." }, { code: "empty", row: null, column: null, message: "Empty." }], totalProblems: 2 }, 400);

    const error = await uploadBatch("Patients", "x", new File(["a"], "x.csv")).catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(problemsOf(error)).toEqual([{ code: "bad_cell", row: 5, column: "DOB", message: "Not a date." }, { code: "empty", row: null, column: null, message: "Empty." }]);
  });

  it("stops waiting after its time limit and says so, but a cancel by the caller stays a cancel", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] });
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => String(url).includes("csrf-token") ? Promise.resolve(json({ token: "t" }))
      : new Promise((_, reject) => init?.signal?.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError"))))));
    const uploads = () => vi.mocked(fetch).mock.calls.filter((c) => !String(c[0]).includes("csrf-token")).length;
    const pending = uploadBatch("Patients", "x", new File(["a"], "x.csv")).catch((e) => e);
    await vi.waitFor(() => expect(uploads()).toBe(1));                                  // the request is on its way, so its time limit is running
    await vi.advanceTimersByTimeAsync(2 * 60 * 1000 + 1);

    const error = await pending;
    expect((error as ApiError).code).toBe("upload_timeout");
    vi.useRealTimers();

    const controller = new AbortController();
    const cancelled = uploadBatch("Patients", "x", new File(["a"], "x.csv"), controller.signal).catch((e) => e);
    await vi.waitFor(() => expect(uploads()).toBe(2));
    controller.abort();
    expect(wasCancelled(await cancelled)).toBe(true);
  });
});

describe("reading and acting on a batch", () => {
  it("builds the row query from the filter, the cursor and the page size", async () => {
    reply = () => json([]);

    await listRows("b1", { status: "Unresolved", after: 40, limit: 50 });
    await listRows("b1", {});

    expect(calls[0].url).toBe("/api/import-batches/b1/rows?status=Unresolved&after=40&limit=50");
    expect(calls[1].url).toBe("/api/import-batches/b1/rows?");
  });

  it("downloads the error report with the server's file name, and turns a refusal into an error", async () => {
    reply = () => new Response(new Blob(["row\r\n"]), { status: 200, headers: { "Content-Disposition": "attachment; filename=import-abc-errors.csv" } });
    expect((await downloadErrorReport("b1")).fileName).toBe("import-abc-errors.csv");

    reply = () => json({ error: "dry_run_required", message: "Run the dry-run first." }, 409);
    const refused = await downloadErrorReport("b1").catch((e) => e);
    expect([refused.status, refused.code]).toEqual([409, "dry_run_required"]);
  });
});

describe("words for refusals", () => {
  it("map the codes a person can act on, and fall back to the server's own sentence", () => {
    expect(importErrorMessage(new ApiError(409, "results_changed", "x"))).toMatch(/results changed/i);
    expect(importErrorMessage(new ApiError(403, "billing_permission_required", "x"))).toMatch(/Manage billing/);
    expect(importErrorMessage(new ApiError(409, "not_stuck", "x"))).toMatch(/not stuck/);
    expect(importErrorMessage(new ApiError(409, "batch_has_problems", "Fix the file."))).toBe("Fix the file.");
    expect(importErrorMessage(new Error("boom"))).toMatch(/Something went wrong/);
  });

  it("read no problems from an error that carries none", () => {
    expect(problemsOf(new ApiError(500, "x", "y"))).toEqual([]);
    expect(problemsOf(new Error("z"))).toEqual([]);
  });
});
