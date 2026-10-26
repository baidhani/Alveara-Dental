/**
 * STORY-018: typed client for taking one patient's record out as a file and bringing one in from a file (Controllers/PatientPortabilityController).
 *
 * - `exportPatientFile` downloads the JSON file (ExportPatientData, the administrator by default); the server logs the export before it sends the file.
 * - `importPatientFile` sends the chosen file as the request body (ImportPatientData, the administrator and the office manager by default) with a CSRF token. A file with problems is NOT stored: the server answers
 *   400 `import_rejected` with every problem to correct, which this client throws as an `ImportRejectedError`; a patient who is already registered is a 409 `ApiError` that carries the record to open.
 * - Every refusal has a stable `error` code (see `portabilityErrorMessage`).
 */
import { ApiError, fetchCsrfToken, request } from "./authApi";

export interface ImportProblem { code: string; path: string; message: string }

export interface ImportResult { patientId: string; created: boolean; exportId: string }

export interface ExportedFile { fileName: string; blob: Blob }

/** A file the server refused to import; `problems` is everything to correct, each with the place in the file. */
export class ImportRejectedError extends Error {
  problems: ImportProblem[];

  constructor(message: string, problems: ImportProblem[]) {
    super(message);
    this.problems = problems;
  }
}

/** The most the server reads (256 KB); a bigger file is refused before it is sent. */
export const MAX_IMPORT_BYTES = 256 * 1024;

const FALLBACK_FILE_NAME = "alveara-patient.json";

function fileNameFrom(disposition: string | null): string {
  const match = disposition?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
  return match ? decodeURIComponent(match[1]) : FALLBACK_FILE_NAME;
}

export async function exportPatientFile(patientId: string, signal?: AbortSignal): Promise<ExportedFile> {
  const res = await fetch(`/api/patients/${encodeURIComponent(patientId)}/export`, { credentials: "include", signal });
  if (!res.ok) {
    let body: Record<string, unknown> = {};
    try { body = (await res.json()) as Record<string, unknown>; } catch { /* the status alone says enough */ }
    throw new ApiError(res.status, (body.error as string) ?? "unknown_error", (body.message as string) ?? `Request failed with status ${res.status}.`, body);
  }
  return { fileName: fileNameFrom(res.headers.get("Content-Disposition")), blob: await res.blob() };
}

export async function importPatientFile(file: File, signal?: AbortSignal): Promise<ImportResult> {
  if (file.size > MAX_IMPORT_BYTES) throw new ImportRejectedError("The file was not imported. Correct the problems listed and send it again.", [{ code: "file_too_large", path: "$", message: `The file is larger than ${MAX_IMPORT_BYTES / 1024} KB, which is more than one patient export can be. Choose a file exported from Alveara.` }]);
  const csrfToken = await fetchCsrfToken();
  try {
    return await request<ImportResult>("/api/patients/import", { method: "POST", headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken }, body: file, signal });
  } catch (e) {
    if (e instanceof ApiError && e.code === "import_rejected" && Array.isArray(e.body.problems)) throw new ImportRejectedError(e.message, e.body.problems as ImportProblem[]);
    throw e;
  }
}

export const portabilityErrorMessage = (e: unknown): string => {
  if (e instanceof ApiError) {
    switch (e.code) {
      case "patient_not_found": return "That patient was not found.";
      case "patient_merged": return "That record was merged into another patient record and is no longer exported; export the record it was merged into.";
      case "duplicate_patient": return "A patient with the same name and date of birth is already registered. Nothing was imported.";
      case "possible_duplicate": return "This patient may be someone who is already registered. Nothing was imported; register the patient from the registration form if this is a different person.";
      case "permission_denied": return "You do not have permission to do that.";
      default: return e.status === 403 ? "You do not have permission to do that." : "That did not work. Try again, and tell an administrator if it keeps happening.";
    }
  }
  return "That did not work. Try again, and tell an administrator if it keeps happening.";
};
