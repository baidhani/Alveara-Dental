/**
 * ALV-018-C01: typed client for the export package endpoints (Controllers/PatientExportsController).
 *
 * - Three downloads, each its own permission and each a POST with a CSRF token: `downloadPatientPackage` (ExportPatientData), `downloadAllPatientsPackage` (ExportAllPatientData) and `downloadReadableRecord`
 *   (ExportPatientReadableRecord). The server writes the history row and the audit entry BEFORE it sends the file, and sends nothing when an export fails. The answer's headers say how it ended
 *   (`X-Export-Result` Complete or Partial, `X-Export-Problems`, `X-Export-Id`).
 * - Every download has an explicit time limit and can be cancelled by the caller; cancelling stops the export on the server too and leaves its history row Failed.
 * - `fetchExportOverview` and `fetchExportHistory` are reads for the Export screen.
 * - Every refusal has a stable `error` code (see `exportErrorMessage`).
 */
import { ApiError, fetchCsrfToken, request } from "./authApi";
import { fileNameFrom } from "./patientPortabilityApi";

export type FolderNaming = "IdOnly" | "NameAndId";

export interface PackageOptions { includeDocumentFiles: boolean; folderNaming?: FolderNaming }

export interface ExportOverview {
  domains: { key: string; title: string }[];
  excluded: { key: string; reason: string }[];
  maxPatients: number;
  maxPackageBytes: number;
  defaultFolderNaming: FolderNaming;
  allPatientsReadMode: "Snapshot" | "TableByTable";
  patientCount: number;
  documentBytes: number;
  allPatientsWithinLimits: boolean;
  practiceTimeZone: string;
}

export interface ExportHistoryItem {
  exportId: string;
  kind: "Package" | "ReadableRecord";
  scope: "OnePatient" | "AllPatients";
  patientId: string | null;
  includesDocumentFiles: boolean;
  requestedBy: string;
  requestedAtUtc: string;
  finishedAtUtc: string | null;
  result: "Started" | "Complete" | "Partial" | "Failed";
  interrupted: boolean;
  patientCount: number;
  fileCount: number;
  sizeBytes: number;
  sha256: string | null;
  problemCount: number;
}

/** A finished download: the file, and how the server says it ended. `problems` is how many things the package lists as missing (0 for a readable record). */
export interface DownloadedExport { fileName: string; blob: Blob; exportId: string; result: "Complete" | "Partial"; problems: number }

/** How long the screen waits for each kind of export before it stops waiting (the server's own limits still apply). */
export const PACKAGE_TIMEOUT_MS = 5 * 60 * 1000;
export const ALL_PATIENTS_TIMEOUT_MS = 15 * 60 * 1000;

const BASE = "/api/patient-exports";

/** True for the error a cancelled download throws (the caller pressed Cancel), as opposed to a time-out, which is an `ApiError`. */
export const wasCancelled = (e: unknown) => e instanceof DOMException && e.name === "AbortError";

async function download(url: string, body: unknown, timeoutMs: number, fallbackName: string, signal?: AbortSignal): Promise<DownloadedExport> {
  const csrf = await fetchCsrfToken();
  const controller = new AbortController();
  let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);
  const cancel = () => controller.abort();
  if (signal?.aborted) controller.abort(); else signal?.addEventListener("abort", cancel, { once: true });
  try {
    const res = await fetch(url, {
      method: "POST", credentials: "include", signal: controller.signal,
      headers: { "Content-Type": "application/json", "X-CSRF-Token": csrf },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!res.ok) {
      let parsed: Record<string, unknown> = {};
      try { parsed = (await res.json()) as Record<string, unknown>; } catch { /* the status alone says enough */ }
      throw new ApiError(res.status, (parsed.error as string) ?? "unknown_error", (parsed.message as string) ?? `Request failed with status ${res.status}.`, parsed);
    }
    const blob = await res.blob();
    return {
      fileName: fileNameFrom(res.headers.get("Content-Disposition"), fallbackName), blob,
      exportId: res.headers.get("X-Export-Id") ?? "",
      result: res.headers.get("X-Export-Result") === "Partial" ? "Partial" : "Complete",
      problems: Number(res.headers.get("X-Export-Problems") ?? "0") || 0,
    };
  } catch (e) {
    if (timedOut && wasCancelled(e)) throw new ApiError(0, "export_timeout", "The export took too long and was stopped.");
    throw e;
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener("abort", cancel);
  }
}

const bodyOf = (options: PackageOptions) => ({ includeDocumentFiles: options.includeDocumentFiles, folderNaming: options.folderNaming });

export const downloadPatientPackage = (patientId: string, options: PackageOptions, signal?: AbortSignal) =>
  download(`${BASE}/patients/${encodeURIComponent(patientId)}/package`, bodyOf(options), PACKAGE_TIMEOUT_MS, "alveara-patient-package.zip", signal);

export const downloadAllPatientsPackage = (options: PackageOptions, signal?: AbortSignal) =>
  download(`${BASE}/all-patients/package`, bodyOf(options), ALL_PATIENTS_TIMEOUT_MS, "alveara-all-patients-package.zip", signal);

export const downloadReadableRecord = (patientId: string, signal?: AbortSignal) =>
  download(`${BASE}/patients/${encodeURIComponent(patientId)}/readable-record`, undefined, PACKAGE_TIMEOUT_MS, "alveara-readable-record.html", signal);

export const fetchExportOverview = (signal?: AbortSignal) => request<ExportOverview>(`${BASE}/overview`, { signal });

/** Newest first. `before` is the requested time of the last line already shown, to get the next older lines. */
export function fetchExportHistory(limit: number, before?: string, signal?: AbortSignal) {
  const query = new URLSearchParams({ limit: String(limit) });
  if (before) query.set("before", before);
  return request<ExportHistoryItem[]>(`${BASE}/history?${query}`, { signal });
}

export const exportErrorMessage = (e: unknown): string => {
  if (e instanceof ApiError) {
    switch (e.code) {
      case "patient_not_found": return "That patient was not found.";
      case "patient_merged": return "That record was merged into another patient record and is no longer exported; export the record it was merged into.";
      case "too_many_patients": return "There are more patients than one export may carry. Nothing was exported.";
      case "package_too_large": return "The package would be larger than one export may carry. Try again without the document files. Nothing was exported.";
      case "export_in_progress": return "Another all-patients export is running. Wait for it to finish, then try again.";
      case "permission_revoked": return "The export was stopped because you no longer have permission to run it. Nothing was delivered.";
      case "permission_policy_unavailable": return "The export was stopped because permissions cannot be checked right now. Try again in a moment. Nothing was delivered.";
      case "invalid_folder_naming": return "Choose one of the folder naming options.";
      case "export_failed": return "The export could not be completed and nothing was delivered. It is recorded in the history as failed. Try again, and tell an administrator if it keeps happening.";
      case "export_timeout": return "The export took too long and was stopped. Nothing was delivered. Try again without the document files, or ask an administrator.";
      case "csrf_token_invalid": return "Your session needs refreshing. Reload the page and try again.";
      default:
        if (e.status === 401) return "Your session has ended. Sign in again.";
        if (e.status === 403) return "You do not have permission to do that.";
        return "That did not work. Try again, and tell an administrator if it keeps happening.";
    }
  }
  return "That did not work. Try again, and tell an administrator if it keeps happening.";
};
