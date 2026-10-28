import { vi } from "vitest";
import type { ImportBatch, ImportRow, MappingSuggestion, Reconciliation } from "../../services/bulkImportApi";

/**
 * A small in-memory server for the data import screens' tests. It answers like the real API (permissions, batch, mapping, dry-run, rows, decision, commit, retry, reconciliation, error report) and records every call.
 * A test changes `server.current`, `server.rows` and so on, or sets `server.reply` to answer a call its own way.
 */
export const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
export const refusal = (status: number, error: string, extra: Record<string, unknown> = {}) => json({ error, message: "Server words.", ...extra }, status);

export type Call = { method: string; url: string; init?: RequestInit };

export const makeBatch = (over: Partial<ImportBatch> = {}): ImportBatch => ({
  id: "b1", sourceKind: "Patients", sourceLabel: "Old system", status: "Uploaded", policy: "AllOrNothing", fileSha256: "ab".repeat(32), fileSizeBytes: 2048, rowCount: 10,
  columnNames: ["First Name", "Last Name", "DOB"], attemptedRows: 0, acceptedRows: 0, rejectedRows: 0, skippedRows: 0, unresolvedRows: 0, createdBy: "Dr. Okafor", createdAtUtc: "2026-10-28T12:00:00+00:00",
  commitOutcome: null, commitStartedAtUtc: null, committedAtUtc: null, commitRound: 0, mappingConfirmed: false, dryRunAtUtc: null, validAmount: 0, acceptedAmount: 0, canRetry: false, ...over,
});

/** A batch that has been mapped and dry-run: 10 rows, 6 valid, 2 rejected, 1 skipped, 1 waiting for a decision. */
export const dryRunBatch = (over: Partial<ImportBatch> = {}) =>
  makeBatch({ status: "DryRunComplete", mappingConfirmed: true, dryRunAtUtc: "2026-10-28T12:05:00+00:00", attemptedRows: 10, rejectedRows: 2, skippedRows: 1, unresolvedRows: 1, ...over });

export const makeRow = (over: Partial<ImportRow> = {}): ImportRow => ({
  rowNumber: 2, status: "Valid", reason: null, errors: null, matchedPatientIds: [], duplicateOfRow: null, resultPatientId: null, decided: false, ...over,
});

export const suggestion = (over: Partial<MappingSuggestion> = {}): MappingSuggestion => ({
  fields: [{ key: "firstName", label: "First name", required: true }, { key: "lastName", label: "Last name", required: true }, { key: "dateOfBirth", label: "Date of birth", required: true }, { key: "phone", label: "Phone", required: false }],
  columns: ["First Name", "Last Name", "DOB"], suggested: { "First Name": "firstName", "Last Name": "lastName", DOB: "dateOfBirth" }, current: null, unmappedColumns: [], missingRequired: [], ...over,
});

export const reconciliation = (over: Partial<Reconciliation> = {}): Reconciliation => ({
  batchId: "b1", sourceKind: "Patients", status: "Committed", policy: "CommitValidRows", commitOutcome: "CompletedWithExceptions", rowCount: 10, accepted: 6, rejected: 2, skipped: 1, unresolved: 1, notCommitted: 0,
  reasons: [{ status: "Accepted", reason: null, rows: 6 }, { status: "Rejected", reason: "invalid_fields", rows: 2 }], recordsFound: 6, recordsMadeByBatch: 6, validAmount: null, acceptedAmount: null, ledgerTotal: null,
  auditEvents: 4, reconciled: true, problems: [], ...over,
});

export class FakeImportServer {
  permissions = ["BulkImportData", "ViewPatientRecords", "ManageBilling"];
  current: ImportBatch = makeBatch();
  batches: ImportBatch[] = [];
  rows: ImportRow[] = [];
  suggested: MappingSuggestion = suggestion();
  report = reconciliation();
  calls: Call[] = [];
  downloads: string[] = [];
  reply: (call: Call) => Response | Promise<Response> | null = () => null;

  install() {
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
      const u = String(url);
      if (u.includes("/api/auth/permissions")) return Promise.resolve(json({ username: "admin", role: "Admin", permissions: this.permissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }));
      if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "t" }));
      const call = { method: init?.method ?? "GET", url: u, init };
      this.calls.push(call);
      return Promise.resolve(this.reply(call) ?? this.answer(call));
    }));
    Object.defineProperty(URL, "createObjectURL", { configurable: true, writable: true, value: vi.fn(() => "blob:test") });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, writable: true, value: vi.fn() });
    const downloads = this.downloads;
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) { downloads.push(this.download); });
  }

  of(method: string, path: RegExp) { return this.calls.filter((c) => c.method === method && path.test(c.url)); }
  bodyOf(call: Call) { return call.init?.body ? JSON.parse(call.init.body as string) : undefined; }

  private answer(c: Call): Response {
    const path = new URL(c.url, "http://x");
    const p = path.pathname;
    if (p === "/api/import-batches" && c.method === "GET") return json(this.batches);
    if (p === "/api/import-batches/orphan-sweep") return json({ found: 3, removed: 1, kept: 2, failed: 0, batchesMissingFile: 0 });
    if (/^\/api\/import-batches\/b1$/.test(p)) return json(this.current);
    if (p.endsWith("/mapping-suggestion")) return json(this.suggested);
    if (p.endsWith("/mapping")) { this.current = { ...this.current, mappingConfirmed: true, dryRunAtUtc: null, status: "Uploaded" }; return json({ ...this.suggested, current: this.suggested.suggested }); }
    if (p.endsWith("/dry-run")) { this.current = dryRunBatch(); return json({ batch: this.current, valid: 6, rejected: 2, skipped: 1, unresolved: 1 }); }
    if (p.endsWith("/rows") && c.method === "GET") {
      const status = path.searchParams.get("status");
      const after = Number(path.searchParams.get("after") ?? 0);
      return json(this.rows.filter((r) => (!status || r.status === status) && r.rowNumber > after).slice(0, Number(path.searchParams.get("limit") ?? 50)));
    }
    const decision = /\/rows\/(\d+)\/decision$/.exec(p);
    if (decision) {
      const row = this.rows.find((r) => r.rowNumber === Number(decision[1]))!;
      const skip = this.bodyOf(c).decision === "Skip";
      Object.assign(row, { status: skip ? "Skipped" : "Valid", decided: true });
      this.current = { ...this.current, unresolvedRows: this.current.unresolvedRows - 1, skippedRows: this.current.skippedRows + (skip ? 1 : 0) };
      return json({ row, batch: this.current });
    }
    if (p.endsWith("/commit")) { this.current = { ...this.current, status: "Committing", commitRound: 1 }; return json(this.current, 202); }
    if (p.endsWith("/retry")) { this.current = { ...this.current, canRetry: false, commitRound: this.current.commitRound + 1 }; return json(this.current, 202); }
    if (p.endsWith("/reconciliation")) return json(this.report);
    if (p.endsWith("/error-report")) return new Response(new Blob(["row,status\r\n"]), { status: 200, headers: { "Content-Type": "text/csv", "Content-Disposition": "attachment; filename=import-b1-errors.csv" } });
    return refusal(404, "not_found");
  }
}
