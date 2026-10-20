import { vi } from "vitest";
import type { PatientDetail } from "../../services/patientsApi";
import type { CategoryView, DocumentDetail, DocumentView, LibraryEntry } from "../../services/documentsApi";
import type { EncounterChoice } from "../../services/documentsApi";
import { installFakeXhr } from "../../test/fakeXhr";
import type { FakeXhrOptions } from "../../test/fakeXhr";

/**
 * Shared by the tests of the Documents tab: a patient, fixtures for documents, library entries, categories and encounters, and a scripted API (`stub`) that records every call. Uploads go through a fake
 * XMLHttpRequest (`installFakeXhr`) because the real client reports progress with one; everything else is a stubbed `fetch`.
 */
export const patient = { id: "p1" } as PatientDetail;
export const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

export const categories: CategoryView[] = [
  { id: "c-rad", code: "radiograph", name: "Radiograph", sortOrder: 10, isActive: true, rowVersion: "r1" },
  { id: "c-photo", code: "photo", name: "Photo", sortOrder: 20, isActive: true, rowVersion: "r2" },
  { id: "c-old", code: "other", name: "Other", sortOrder: 90, isActive: false, rowVersion: "r3" },
];

export const encounterOf = (id: string, atUtc: string): EncounterChoice => ({ id, encounterAtUtc: atUtc, status: "Finalized" });
export const encounters: EncounterChoice[] = [encounterOf("e1", "2030-03-09T15:00:00Z"), encounterOf("e2", "2030-02-01T15:00:00Z")];

export const docOf = (over: Partial<DocumentView> = {}): DocumentView => ({
  id: "d1", patientId: "p1", categoryId: "c-rad", categoryCode: "radiograph", categoryName: "Radiograph", documentDate: "2030-03-09", description: "Bitewing, left side.", duplicateOfDocumentId: null,
  originalFileName: "bitewing.png", detectedType: "png", typeVerified: true, sizeBytes: 2048, sha256: "a".repeat(64), importedByName: "Olive Office", importedAtUtc: "2030-03-10T10:00:00Z", rowVersion: "v1",
  encounterId: null, toothKey: null, regionKey: null, status: "Active", inactiveReason: null, inactivatedByName: null, inactivatedAtUtc: null, integrityStatus: "Unchecked", integrityCheckedAtUtc: null, ...over,
});

export const entryOf = (d: DocumentView, over: Partial<LibraryEntry> = {}): LibraryEntry => ({
  kind: "Document", id: d.id, patientFormId: null, title: d.originalFileName, categoryName: d.categoryName, date: d.documentDate, detectedType: d.detectedType, sizeBytes: d.sizeBytes,
  importedByName: d.importedByName, atUtc: d.importedAtUtc, isVoided: false, isDuplicate: d.duplicateOfDocumentId !== null, document: d, isInactive: d.status === "Inactive", ...over,
});

export const signedForm = (over: Partial<LibraryEntry> = {}): LibraryEntry => ({
  kind: "SignedForm", id: "snap1", patientFormId: "f1", title: "Privacy notice", categoryName: "Signed form", date: "2030-03-01", detectedType: "form", sizeBytes: null, importedByName: null,
  atUtc: "2030-03-01T09:00:00Z", isVoided: false, isDuplicate: false, document: null, isInactive: false, ...over,
});

export const detailOf = (d: DocumentView, over: Partial<DocumentDetail> = {}): DocumentDetail => ({
  document: d, history: [{ eventNumber: 1, kind: "Imported", atUtc: d.importedAtUtc, byName: d.importedByName, fromCategoryName: null, toCategoryName: null, relatedDocumentId: null }], statusChanges: [], ...over,
});

export interface Call { url: string; method: string; headers: Record<string, string>; form: FormData | null; body: Record<string, unknown> | null }
export interface Script {
  entries: LibraryEntry[]; max: number; loadFails: boolean; encounters: EncounterChoice[];
  import: (c: Call) => Response | Promise<Response>; edit: (c: Call) => Response;
  inactivate: (id: string, c: Call) => Response; reactivate: (id: string, c: Call) => Response; detail: (id: string) => Response;
}

export function stub(over: Partial<Script> = {}, xhr: FakeXhrOptions = {}) {
  const script: Script = {
    entries: [], max: 100 * 1024 * 1024, loadFails: false, encounters,
    import: () => json({ document: docOf({ id: "d-new", originalFileName: "new.png" }), replayed: false }, 201),
    edit: () => json({ document: docOf({ rowVersion: "v2" }) }),
    inactivate: () => json({ document: docOf({ status: "Inactive", inactiveReason: "Why.", rowVersion: "v3" }) }),
    reactivate: () => json({ document: docOf({ rowVersion: "v4" }) }),
    detail: (id) => json(detailOf(docOf({ id }))),
    ...over,
  };
  const calls: Call[] = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const headers = (init?.headers ?? {}) as Record<string, string>;
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    const call: Call = { url: u, method, headers, form: null, body };
    calls.push(call);
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (u.includes("/api/document-categories")) return Promise.resolve(json({ categories: u.includes("includeInactive") ? categories : categories.filter((c) => c.isActive) }));
    if (u.includes("/api/patients/p1/documents/encounters")) return Promise.resolve(json(script.encounters));
    if (u.includes("/api/patients/p1/documents") && method === "GET") return Promise.resolve(script.loadFails ? json({ error: "server_error" }, 500) : json({ patientId: "p1", entries: script.entries, maxUploadBytes: script.max }));
    if (u.endsWith("/inactivate") && method === "POST") return Promise.resolve(script.inactivate(u.split("/").slice(-2)[0], call));
    if (u.endsWith("/reactivate") && method === "POST") return Promise.resolve(script.reactivate(u.split("/").slice(-2)[0], call));
    if (/\/api\/documents\/[^/]+$/.test(u) && method === "GET") return Promise.resolve(script.detail(u.split("/").pop()!));
    if (u.includes("/api/documents/") && method === "PUT") return Promise.resolve(script.edit(call));
    return Promise.resolve(json({}));
  }));
  const uploads = installFakeXhr((c) => script.import({ ...c, body: null }), xhr);
  const lists = () => calls.filter((c) => c.method === "GET" && c.url.includes("/api/patients/p1/documents"));
  return {
    script, calls, uploads,
    imports: () => uploads.calls.map((c) => ({ ...c, body: null }) as Call),
    edits: () => calls.filter((c) => c.method === "PUT"),
    lists,
    lastList: () => decodeURIComponent(lists().at(-1)?.url ?? ""),
    posts: (suffix: string) => calls.filter((c) => c.method === "POST" && c.url.endsWith(suffix)),
  };
}
