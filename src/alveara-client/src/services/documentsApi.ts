/**
 * STORY-010: typed client for documents (Controllers/DocumentsController, docs/DOCUMENTS.md).
 *
 * - Importing sends the file as multipart with an `Idempotency-Key`; the same key is reused for a retry of the same attempt, so a retry can never import the file twice. A file the patient already has is refused
 *   with 409 `duplicate_document` (the error's `body.detail` names the existing document) and is added only by sending the same file again with `confirmDuplicateOf`.
 * - Every refusal carries a stable `error` code and, for wrong details, `problems` naming each field.
 * - A file is opened or downloaded by plain navigation to `contentUrl` (the session cookie authorizes it); the server decides whether a type may be shown in the browser, never this client.
 * - Nothing here ever receives or builds a storage path.
 */
import { ApiError, fetchCsrfToken, request, requestWithCsrf } from "./authApi";

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
}

export interface DocumentLibrary { patientId: string; entries: LibraryEntry[]; maxUploadBytes: number }
export interface HistoryEntry { eventNumber: number; kind: string; atUtc: string; byName: string; fromCategoryName: string | null; toCategoryName: string | null; relatedDocumentId: string | null }
export interface DocumentDetail { document: DocumentView; history: HistoryEntry[] }
export interface DocumentProblem { field: string; code: string; message: string }
export interface DuplicateSummary { documentId: string; categoryName: string; documentDate: string; importedByName: string; importedAtUtc: string }
export interface CategoryView { id: string; code: string; name: string; sortOrder: number; isActive: boolean; rowVersion: string }
export interface DocumentSettings { maxUploadBytes: number; minAllowedBytes: number; maxAllowedBytes: number; rowVersion: string }
export interface BlockedExtension { extension: string; isBuiltIn: boolean; addedAtUtc: string }

export interface ImportInput {
  file: File;
  categoryId: string;
  documentDate: string;
  description: string;
  /** Stays the same for every retry of one attempt. */
  key: string;
  confirmDuplicateOf?: string;
}

export interface EditInput { categoryId: string; documentDate: string; description: string; rowVersion: string }

export function newKey(): string {
  return crypto.randomUUID();
}

const base = (patientId: string) => `/api/patients/${patientId}/documents`;

export const contentUrl = (documentId: string, download = false) => `/api/documents/${documentId}/content${download ? "?download=true" : ""}`;

export function listLibrary(patientId: string, categoryId?: string, signal?: AbortSignal): Promise<DocumentLibrary> {
  const query = categoryId ? `?categoryId=${encodeURIComponent(categoryId)}` : "";
  return request<DocumentLibrary>(`${base(patientId)}${query}`, { signal });
}

export function getDocument(documentId: string, signal?: AbortSignal): Promise<DocumentDetail> {
  return request<DocumentDetail>(`/api/documents/${documentId}`, { signal });
}

export async function importDocument(patientId: string, input: ImportInput): Promise<{ document: DocumentView; replayed: boolean }> {
  const form = new FormData();
  form.append("file", input.file, input.file.name);
  form.append("categoryId", input.categoryId);
  form.append("documentDate", input.documentDate);
  form.append("description", input.description);
  if (input.confirmDuplicateOf) form.append("confirmDuplicateOf", input.confirmDuplicateOf);
  const csrf = await fetchCsrfToken();
  return request(base(patientId), { method: "POST", headers: { "X-CSRF-Token": csrf, "Idempotency-Key": input.key }, body: form });
}

export async function editDocument(documentId: string, input: EditInput): Promise<DocumentView> {
  const r = await requestWithCsrf<{ document: DocumentView }>(`/api/documents/${documentId}`, "PUT", input);
  return r.document;
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

/** The problems an error carries, by field. */
export function problemsOf(err: unknown): DocumentProblem[] {
  return err instanceof ApiError && Array.isArray(err.body.problems) ? (err.body.problems as DocumentProblem[]) : [];
}

export function duplicateOf(err: unknown): DuplicateSummary | null {
  return err instanceof ApiError && err.status === 409 && err.code === "duplicate_document" ? (err.body.detail as DuplicateSummary) : null;
}
