/**
 * STORY-010 and ALV-010-C01: typed client for documents (Controllers/DocumentsController and DocumentStorageController, docs/DOCUMENTS.md).
 *
 * - Importing sends the file as multipart with an `Idempotency-Key` and reports upload progress; the same key is reused for a retry of the same attempt, so a retry can never import the file twice. A file the patient
 *   already has is refused with 409 `duplicate_document` (the error's `body.detail` names the existing document) and is added only by sending the same file again with `confirmDuplicateOf`.
 * - A document may name an encounter of the patient and one tooth or one oral region (never both); an edit states all of them and a field left empty is cleared.
 * - A document is never deleted: it is made inactive with a reason (hidden from the library by default) and can be made active again. The reason is kept in the status history.
 * - Every refusal carries a stable `error` code and, for wrong details, `problems` naming each field.
 * - A file is opened or downloaded by plain navigation to `contentUrl` (the session cookie authorizes it); the server decides whether a type may be shown in the browser, never this client. A thumbnail is
 *   `thumbnailUrl` (PNG and JPEG up to 5 MB; opening it is not logged, an explicit open is).
 * - The storage check and the review of files nothing points to are the `Storage` functions; they need the storage permission.
 * - Nothing here ever receives or builds a storage path.
 */
import { ApiError, fetchCsrfToken, reportUnauthorized, request, requestWithCsrf } from "./authApi";

export interface DocumentView {
  id: string;
  patientId: string;
  categoryId: string;
  categoryCode: string;
  categoryName: string;
  documentDate: string;
  description: string | null;
  duplicateOfDocumentId: string | null;
  originalFileName: string;
  detectedType: string;
  typeVerified: boolean;
  sizeBytes: number;
  sha256: string;
  importedByName: string;
  importedAtUtc: string;
  rowVersion: string;
  encounterId: string | null;
  toothKey: string | null;
  regionKey: string | null;
  status: "Active" | "Inactive";
  inactiveReason: string | null;
  inactivatedByName: string | null;
  inactivatedAtUtc: string | null;
  integrityStatus: "Unchecked" | "Ok" | "Missing" | "Corrupt";
  integrityCheckedAtUtc: string | null;
}

export interface LibraryEntry {
  kind: "Document" | "SignedForm";
  id: string;
  patientFormId: string | null;
  title: string;
  categoryName: string;
  date: string;
  detectedType: string;
  sizeBytes: number | null;
  importedByName: string | null;
  atUtc: string;
  isVoided: boolean;
  isDuplicate: boolean;
  document: DocumentView | null;
  isInactive: boolean;
}

export interface DocumentLibrary { patientId: string; entries: LibraryEntry[]; maxUploadBytes: number }
export interface HistoryEntry { eventNumber: number; kind: string; atUtc: string; byName: string; fromCategoryName: string | null; toCategoryName: string | null; relatedDocumentId: string | null }
export interface StatusChange { toStatus: "Active" | "Inactive"; reason: string | null; byName: string; atUtc: string }
export interface DocumentDetail { document: DocumentView; history: HistoryEntry[]; statusChanges: StatusChange[] | null }
export interface DocumentProblem { field: string; code: string; message: string }
export interface DuplicateSummary { documentId: string; categoryName: string; documentDate: string; importedByName: string; importedAtUtc: string }
export interface CategoryView { id: string; code: string; name: string; sortOrder: number; isActive: boolean; rowVersion: string }
export interface DocumentSettings { maxUploadBytes: number; minAllowedBytes: number; maxAllowedBytes: number; rowVersion: string }
export interface BlockedExtension { extension: string; isBuiltIn: boolean; addedAtUtc: string }

/** What narrows the library. Every field is optional and they combine. `status` is Active (the default), Inactive or All. */
export interface LibraryFilter {
  categoryId?: string;
  encounterId?: string;
  toothKey?: string;
  regionKey?: string;
  from?: string;
  to?: string;
  text?: string;
  status?: "Active" | "Inactive" | "All";
}

/** The clinical context of a document: an encounter of the patient and one tooth or one region. Empty strings mean none. */
export interface DocumentContext { encounterId: string; toothKey: string; regionKey: string }

export interface ImportInput extends DocumentContext {
  file: File;
  categoryId: string;
  documentDate: string;
  description: string;
  /** Stays the same for every retry of one attempt. */
  key: string;
  confirmDuplicateOf?: string;
}

export interface EditInput extends DocumentContext { categoryId: string; documentDate: string; description: string; rowVersion: string }

export function newKey(): string {
  return crypto.randomUUID();
}

const base = (patientId: string) => `/api/patients/${patientId}/documents`;

export const contentUrl = (documentId: string, download = false) => `/api/documents/${documentId}/content${download ? "?download=true" : ""}`;
export const thumbnailUrl = (documentId: string) => `/api/documents/${documentId}/thumbnail`;

/** The types and the size a thumbnail is offered for (the server enforces the same rule). */
export const THUMBNAIL_TYPES: ReadonlySet<string> = new Set(["png", "jpeg"]);
export const THUMBNAIL_MAX_BYTES = 5 * 1024 * 1024;

export function listLibrary(patientId: string, filter: LibraryFilter = {}, signal?: AbortSignal): Promise<DocumentLibrary> {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(filter)) if (value) query.set(key, value);
  const text = query.toString();
  return request<DocumentLibrary>(`${base(patientId)}${text ? `?${text}` : ""}`, { signal });
}

export function getDocument(documentId: string, signal?: AbortSignal): Promise<DocumentDetail> {
  return request<DocumentDetail>(`/api/documents/${documentId}`, { signal });
}

/**
 * Sends the file and its details, reporting how much of the upload has gone (0 to 1). Aborting the signal cancels the upload (the promise rejects with an AbortError); a cancelled or failed attempt can be repeated
 * with the same key, so it never imports twice.
 */
export async function importDocument(patientId: string, input: ImportInput, onProgress?: (fraction: number) => void, signal?: AbortSignal): Promise<{ document: DocumentView; replayed: boolean }> {
  const form = new FormData();
  form.append("file", input.file, input.file.name);
  form.append("categoryId", input.categoryId);
  form.append("documentDate", input.documentDate);
  form.append("description", input.description);
  if (input.encounterId) form.append("encounterId", input.encounterId);
  if (input.toothKey) form.append("toothKey", input.toothKey);
  if (input.regionKey) form.append("regionKey", input.regionKey);
  if (input.confirmDuplicateOf) form.append("confirmDuplicateOf", input.confirmDuplicateOf);
  const csrf = await fetchCsrfToken();

  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", base(patientId));
    xhr.withCredentials = true;
    xhr.timeout = 30 * 60 * 1000;
    xhr.setRequestHeader("X-CSRF-Token", csrf);
    xhr.setRequestHeader("Idempotency-Key", input.key);
    xhr.upload.onprogress = (e) => { if (e.lengthComputable && e.total > 0) onProgress?.(e.loaded / e.total); };
    xhr.onload = () => {
      let body: Record<string, unknown> = {};
      try { body = xhr.responseText ? (JSON.parse(xhr.responseText) as Record<string, unknown>) : {}; } catch { body = {}; }
      if (xhr.status >= 200 && xhr.status < 300) { resolve(body as unknown as { document: DocumentView; replayed: boolean }); return; }
      if (xhr.status === 401) reportUnauthorized();
      reject(new ApiError(xhr.status, (body.error as string) ?? "unknown_error", (body.message as string) ?? `Request failed with status ${xhr.status}.`, body));
    };
    xhr.onerror = () => reject(new TypeError("The upload could not reach the server."));
    xhr.ontimeout = () => reject(new TypeError("The upload took too long."));
    xhr.onabort = () => reject(new DOMException("The upload was cancelled.", "AbortError"));
    if (signal) {
      if (signal.aborted) { reject(new DOMException("The upload was cancelled.", "AbortError")); return; }
      signal.addEventListener("abort", () => xhr.abort(), { once: true });
    }
    xhr.send(form);
  });
}

export async function editDocument(documentId: string, input: EditInput): Promise<DocumentView> {
  const body = { categoryId: input.categoryId, documentDate: input.documentDate, description: input.description, rowVersion: input.rowVersion,
    encounterId: input.encounterId || null, toothKey: input.toothKey || null, regionKey: input.regionKey || null };
  const r = await requestWithCsrf<{ document: DocumentView }>(`/api/documents/${documentId}`, "PUT", body);
  return r.document;
}

/** Hides the document from the library (it is never deleted). Needs a reason; repeating it changes nothing. */
export async function inactivateDocument(documentId: string, reason: string, rowVersion: string): Promise<DocumentView> {
  return (await requestWithCsrf<{ document: DocumentView }>(`/api/documents/${documentId}/inactivate`, "POST", { reason, rowVersion })).document;
}

export async function reactivateDocument(documentId: string, rowVersion: string): Promise<DocumentView> {
  return (await requestWithCsrf<{ document: DocumentView }>(`/api/documents/${documentId}/reactivate`, "POST", { rowVersion })).document;
}

export async function listCategories(includeInactive = false, signal?: AbortSignal): Promise<CategoryView[]> {
  return (await request<{ categories: CategoryView[] }>(`/api/document-categories${includeInactive ? "?includeInactive=true" : ""}`, { signal })).categories;
}

export async function addCategory(name: string): Promise<CategoryView> {
  return (await requestWithCsrf<{ category: CategoryView }>("/api/document-categories", "POST", { name })).category;
}

export async function editCategory(id: string, change: { name?: string; isActive?: boolean; rowVersion: string }): Promise<CategoryView> {
  return (await requestWithCsrf<{ category: CategoryView }>(`/api/document-categories/${id}`, "PUT", change)).category;
}

export function getSettings(signal?: AbortSignal): Promise<DocumentSettings> {
  return request<DocumentSettings>("/api/document-settings", { signal });
}

export function setSizeLimit(maxUploadBytes: number, rowVersion: string): Promise<DocumentSettings> {
  return requestWithCsrf<DocumentSettings>("/api/document-settings", "PUT", { maxUploadBytes, rowVersion });
}

export async function listBlocked(signal?: AbortSignal): Promise<BlockedExtension[]> {
  return (await request<{ blockedExtensions: BlockedExtension[] }>("/api/document-settings/blocked-extensions", { signal })).blockedExtensions;
}

export async function addBlocked(extension: string): Promise<BlockedExtension> {
  return (await requestWithCsrf<{ blockedExtension: BlockedExtension }>("/api/document-settings/blocked-extensions", "POST", { extension })).blockedExtension;
}

export async function removeBlocked(extension: string): Promise<void> {
  await requestWithCsrf<void>(`/api/document-settings/blocked-extensions/${encodeURIComponent(extension)}`, "DELETE");
}

// ---------- storage ----------

export interface StorageProblem { documentId: string; patientId: string; fileName: string; status: "Missing" | "Corrupt"; checkedAtUtc: string }
export interface OrphanFile { blobId: string; sizeBytes: number; lastWriteUtc: string }
export interface StorageSummary { documents: number; ok: number; missing: number; corrupt: number; neverChecked: number; problems: StorageProblem[]; orphans: OrphanFile[] }
export interface StorageReport { checked: number; ok: number; missing: number; corrupt: number; skipped: number; problems: StorageProblem[]; orphans: OrphanFile[]; ranAtUtc: string }
export type OrphanOutcome = "Removed" | "AlreadyGone" | "StillReferenced" | "TooNew" | "Failed";
export interface OrphanRemovalResult { results: { blobId: string; outcome: OrphanOutcome }[]; removed: number }

export function getStorageSummary(signal?: AbortSignal): Promise<StorageSummary> {
  return request<StorageSummary>("/api/document-storage", { signal });
}

/** Checks every stored file against the size and hash recorded at import. It reads every file, so it takes time in proportion to the library. */
export function runStorageCheck(): Promise<StorageReport> {
  return requestWithCsrf<StorageReport>("/api/document-storage/check", "POST");
}

export function checkDocumentStorage(documentId: string): Promise<{ documentId: string; status: "Ok" | "Missing" | "Corrupt"; checkedAtUtc: string }> {
  return requestWithCsrf(`/api/document-storage/documents/${documentId}/check`, "POST");
}

/** Removes the stored files nothing points to that a reviewer named (up to 500); each gets its own outcome. */
export function removeOrphans(blobIds: string[]): Promise<OrphanRemovalResult> {
  return requestWithCsrf<OrphanRemovalResult>("/api/document-storage/orphans/remove", "POST", { blobIds });
}

/** The problems an error carries, by field. */
export function problemsOf(err: unknown): DocumentProblem[] {
  return err instanceof ApiError && Array.isArray(err.body.problems) ? (err.body.problems as DocumentProblem[]) : [];
}

export function duplicateOf(err: unknown): DuplicateSummary | null {
  return err instanceof ApiError && err.status === 409 && err.code === "duplicate_document" ? (err.body.detail as DuplicateSummary) : null;
}
