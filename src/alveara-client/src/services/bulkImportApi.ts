/**
 * ALV-018-C02: typed client for bulk import and migration (Controllers/ImportBatchesController). A batch is one CSV file of patients, appointments or opening balances: it is uploaded, its columns are mapped, a dry-run
 * checks every row without writing anything, likely duplicate patients are decided by a person, and only then is the batch committed. Nothing here ever carries a value from the file: the server returns counts, ids,
 * column names and fixed messages. Every call needs BulkImportData; a balance commit or retry also needs ManageBilling. Every refusal has a stable `error` code (see `importErrorMessage`).
 */
import { ApiError, fetchCsrfToken, request, requestWithCsrf } from "./authApi";
import { fileNameFrom } from "./patientPortabilityApi";

export type ImportKind = "Patients" | "Appointments" | "OpeningBalances";
export type ImportPolicy = "AllOrNothing" | "CommitValidRows";
export type RowStatus = "Valid" | "Rejected" | "Unresolved" | "Skipped" | "Accepted";
export type RowDecision = "RegisterAsNew" | "Skip";

export interface ImportBatch {
  id: string;
  sourceKind: ImportKind;
  sourceLabel: string;
  status: "Uploaded" | "DryRunComplete" | "Committing" | "Committed" | "Failed" | "Cancelled";
  policy: ImportPolicy;
  fileSha256: string;
  fileSizeBytes: number;
  rowCount: number;
  columnNames: string[];
  attemptedRows: number;
  acceptedRows: number;
  rejectedRows: number;
  skippedRows: number;
  unresolvedRows: number;
  createdBy: string;
  createdAtUtc: string;
  commitOutcome: "Completed" | "CompletedWithExceptions" | "Refused" | "Interrupted" | null;
  commitStartedAtUtc: string | null;
  committedAtUtc: string | null;
  commitRound: number;
  mappingConfirmed: boolean;
  dryRunAtUtc: string | null;
  validAmount: number;
  acceptedAmount: number;
  canRetry: boolean;
}

export interface ImportProblem { code: string; row: number | null; column: string | null; message: string }
export interface ImportUpload { batch: ImportBatch; created: boolean }
export interface ImportField { key: string; label: string; required: boolean }

export interface MappingSuggestion {
  fields: ImportField[];
  columns: string[];
  suggested: Record<string, string>;
  current: Record<string, string> | null;
  unmappedColumns: string[];
  missingRequired: string[];
}

export interface DryRunResult { batch: ImportBatch; valid: number; rejected: number; skipped: number; unresolved: number }

export interface ImportRow {
  rowNumber: number;
  status: RowStatus;
  reason: string | null;
  errors: Record<string, string> | null;
  matchedPatientIds: string[];
  duplicateOfRow: number | null;
  resultPatientId: string | null;
  decided: boolean;
}

export interface Reconciliation {
  batchId: string;
  sourceKind: ImportKind;
  status: string;
  policy: ImportPolicy;
  commitOutcome: string | null;
  rowCount: number;
  accepted: number;
  rejected: number;
  skipped: number;
  unresolved: number;
  notCommitted: number;
  reasons: { status: string; reason: string | null; rows: number }[];
  recordsFound: number;
  recordsMadeByBatch: number;
  validAmount: number | null;
  acceptedAmount: number | null;
  ledgerTotal: number | null;
  auditEvents: number;
  reconciled: boolean;
  problems: { code: string; message: string }[];
}

export interface SweepResult { found: number; removed: number; kept: number; failed: number; batchesMissingFile: number }
export interface ErrorReportFile { fileName: string; blob: Blob }

const BASE = "/api/import-batches";
export const UPLOAD_TIMEOUT_MS = 2 * 60 * 1000;

/** True for the error a cancelled request throws (the caller left the page), as opposed to a time-out, which is an `ApiError`. */
export const wasCancelled = (e: unknown) => e instanceof DOMException && e.name === "AbortError";

/** The problems a refused file or mapping lists (`import_file_rejected` and `mapping_invalid` carry them); an empty list for any other error. */
export function problemsOf(e: unknown): ImportProblem[] {
  if (!(e instanceof ApiError) || !Array.isArray(e.body.problems)) return [];
  return (e.body.problems as Record<string, unknown>[]).map((p) => ({
    code: String(p.code ?? ""), row: (p.row as number | null | undefined) ?? null, column: (p.column as string | null | undefined) ?? null, message: String(p.message ?? ""),
  }));
}

/** Sends the CSV as the request body (the server reads the file itself, so there is no multipart wrapper). A refused file throws an `ApiError` whose `problems` are read with `problemsOf`. */
export async function uploadBatch(kind: ImportKind, label: string, file: File, signal?: AbortSignal): Promise<ImportUpload> {
  const csrf = await fetchCsrfToken();
  const controller = new AbortController();
  let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, UPLOAD_TIMEOUT_MS);
  const cancel = () => controller.abort();
  if (signal?.aborted) controller.abort(); else signal?.addEventListener("abort", cancel, { once: true });
  try {
    return await request<ImportUpload>(`${BASE}?${new URLSearchParams({ sourceKind: kind, label })}`, {
      method: "POST", signal: controller.signal, headers: { "Content-Type": "text/csv", "X-CSRF-Token": csrf }, body: file,
    });
  } catch (e) {
    if (timedOut && wasCancelled(e)) throw new ApiError(0, "upload_timeout", "The upload took too long and was stopped.");
    throw e;
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener("abort", cancel);
  }
}

export const listBatches = (limit: number, before?: string, signal?: AbortSignal) => {
  const query = new URLSearchParams({ limit: String(limit) });
  if (before) query.set("before", before);
  return request<ImportBatch[]>(`${BASE}?${query}`, { signal });
};

export const getBatch = (id: string, signal?: AbortSignal) => request<ImportBatch>(`${BASE}/${id}`, { signal });
export const getMappingSuggestion = (id: string, signal?: AbortSignal) => request<MappingSuggestion>(`${BASE}/${id}/mapping-suggestion`, { signal });
export const saveMapping = (id: string, mapping: Record<string, string | null>) => requestWithCsrf<MappingSuggestion>(`${BASE}/${id}/mapping`, "PUT", { mapping });
export const runDryRun = (id: string) => requestWithCsrf<DryRunResult>(`${BASE}/${id}/dry-run`, "POST");

export function listRows(id: string, options: { status?: RowStatus; after?: number; limit?: number }, signal?: AbortSignal) {
  const query = new URLSearchParams();
  if (options.status) query.set("status", options.status);
  if (options.after) query.set("after", String(options.after));
  if (options.limit) query.set("limit", String(options.limit));
  return request<ImportRow[]>(`${BASE}/${id}/rows?${query}`, { signal });
}

export const decideRow = (id: string, rowNumber: number, decision: RowDecision) =>
  requestWithCsrf<{ row: ImportRow; batch: ImportBatch }>(`${BASE}/${id}/rows/${rowNumber}/decision`, "POST", { decision });

export const commitBatch = (id: string, policy: ImportPolicy, expectedValidRows: number) =>
  requestWithCsrf<ImportBatch>(`${BASE}/${id}/commit`, "POST", { policy, expectedValidRows });

export const retryCommit = (id: string) => requestWithCsrf<ImportBatch>(`${BASE}/${id}/retry`, "POST");
export const getReconciliation = (id: string, signal?: AbortSignal) => request<Reconciliation>(`${BASE}/${id}/reconciliation`, { signal });
export const sweepOrphanFiles = () => requestWithCsrf<SweepResult>(`${BASE}/orphan-sweep`, "POST");

export async function downloadErrorReport(id: string): Promise<ErrorReportFile> {
  const res = await fetch(`${BASE}/${id}/error-report`, { credentials: "include" });
  if (!res.ok) {
    let parsed: Record<string, unknown> = {};
    try { parsed = (await res.json()) as Record<string, unknown>; } catch { /* the status alone says enough */ }
    throw new ApiError(res.status, (parsed.error as string) ?? "unknown_error", (parsed.message as string) ?? `Request failed with status ${res.status}.`, parsed);
  }
  return { fileName: fileNameFrom(res.headers.get("Content-Disposition"), "import-errors.csv"), blob: await res.blob() };
}

/** Words for the refusals a person can act on; any other refusal shows the server's own fixed sentence. */
export function importErrorMessage(e: unknown): string {
  if (!(e instanceof ApiError)) return "Something went wrong. Try again; if it keeps happening, contact support.";
  switch (e.code) {
    case "upload_timeout": return "The upload took too long and was stopped. Nothing was imported.";
    case "import_file_rejected": return "The file was not accepted. Correct the problems listed and choose it again.";
    case "mapping_invalid": return "The mapping was not saved. Correct the problems listed and save it again.";
    case "results_changed": return "The results changed since you looked at them, so nothing was committed. The page now shows the current figures; check them and confirm again.";
    case "batch_changed": return "Someone else changed this batch. The page now shows its current state.";
    case "billing_permission_required": return "Loading opening balances also needs the Manage billing permission, which your role does not hold.";
    case "not_stuck": return "This commit is not stuck, so it cannot be retried. Wait for it to finish or refresh the page.";
    case "dry_run_required": return "Run the dry-run first.";
    case "mapping_required": return "Confirm the column mapping first.";
    case "permission_denied": return "Your role does not hold the permission this needs.";
    default: return e.message;
  }
}
