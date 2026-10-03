import { FakePatientServer, json } from "./fakePatientServer";
import type { Call } from "./fakePatientServer";
import type { EncounterAddendum, EncounterDetail, EncounterEntry, EncounterEvent, EncounterSection, EncounterSummary } from "../services/clinicalApi";
import { SECTION_ORDER } from "../services/clinicalApi";

/**
 * STORY-005: an in-memory stand-in for the clinical documentation API, on top of the fake patient server. It models the response SHAPES (same as
 * Controllers/ClinicalController), row-version checking (a stale write is the shared 409 concurrency conflict), the section and finalize rules (so the UI's handling of
 * "documentation incomplete", "finalized" and "duplicate" is exercised), the idempotent start and addendum (the same key replays the first result), and canned failures -
 * including `dropNextResponse`, which STORES the change and then fails the response like a dropped connection. The real rules are proven by the backend tests and the
 * real-backend walkthrough; this exists so the UI's handling of each outcome can be tested in isolation.
 */
interface StoredEncounter {
  id: string; patientId: string; appointmentId: string | null; at: string; status: "Draft" | "Finalized"; v: number; startKey: string | null;
  entries: (EncounterEntry & { removed?: boolean })[]; marks: Set<string>; addenda: (EncounterAddendum & { key: string })[]; events: EncounterEvent[];
  finalizedAtUtc: string | null;
}

const SEVERITIES = ["Mild", "Moderate", "Severe"];

export class FakeClinicalServer extends FakePatientServer {
  encounters = new Map<string, StoredEncounter>();
  clinicalCalls: Call[] = [];
  private injectedClinical = new Map<string, Response[]>();
  private dropNext = new Set<string>();
  private gates = new Map<string, Promise<void>>();
  private seq = 0;
  /** A stand-in for another user changing an encounter behind the screen's back: bumps the version without touching the content. */
  touch(id: string) { this.encounters.get(id)!.v++; }

  constructor() {
    super();
    this.permissions = ["ViewPatientRecords", "RegisterPatients", "EditPatients", "ViewClinicalDocumentation", "ManageClinicalNotes"];
  }

  /** The NEXT request matching `METHOD path-prefix` gets this response (for example a 500). */
  failClinical(methodAndPath: string, response: Response) {
    this.injectedClinical.set(methodAndPath, [...(this.injectedClinical.get(methodAndPath) ?? []), response]);
  }
  /** The NEXT request matching `METHOD path-suffix` is processed and STORED, then its response is lost (the connection drops). */
  dropNextResponse(methodAndPathSuffix: string) { this.dropNext.add(methodAndPathSuffix); }

  /** Holds the NEXT request matching `METHOD path-prefix` until the returned function is called (a slow server), so a test can act while it is in flight. */
  hold(methodAndPath: string): () => void {
    let release!: () => void;
    this.gates.set(methodAndPath, new Promise<void>((resolve) => (release = resolve)));
    return release;
  }

  callsToClinical(method: string, pathPart: string) {
    return this.clinicalCalls.filter((c) => c.method === method && new URL(c.url, "http://x").pathname.includes(pathPart));
  }

  addEncounter(patientId: string, over: Partial<{ id: string; status: "Draft" | "Finalized"; at: string; entries: Partial<EncounterEntry>[]; none: string[]; addenda: string[] }> = {}): StoredEncounter {
    const e: StoredEncounter = {
      id: over.id ?? `enc-${++this.seq}`, patientId, appointmentId: null, at: over.at ?? "2026-10-02T15:00:00Z", status: "Draft", v: 1, startKey: null,
      entries: [], marks: new Set(over.none ?? []), addenda: [], events: [{ eventType: "Created", actorUserId: "u1", occurredAtUtc: "2026-10-02T15:00:00Z", detail: "Encounter started." }], finalizedAtUtc: null,
    };
    for (const x of over.entries ?? []) e.entries.push(this.makeEntry(x));
    for (const text of over.addenda ?? []) e.addenda.push({ id: `a-${++this.seq}`, text, createdAtUtc: "2026-10-03T09:00:00Z", createdByUserId: "u1", key: `k-${this.seq}` });
    if (over.status === "Finalized") { e.status = "Finalized"; e.finalizedAtUtc = "2026-10-02T16:00:00Z"; }
    this.encounters.set(e.id, e);
    return e;
  }

  private makeEntry(x: Partial<EncounterEntry>): EncounterEntry {
    return { id: x.id ?? `en-${++this.seq}`, kind: x.kind ?? "Allergy", name: x.name ?? "Penicillin", detail: x.detail ?? null, reaction: x.reaction ?? null, severity: x.severity ?? null,
      dose: x.dose ?? null, frequency: x.frequency ?? null, createdAtUtc: "2026-10-02T15:05:00Z", createdByUserId: "u1", updatedAtUtc: null };
  }

  private view(e: StoredEncounter): EncounterDetail {
    const sections: EncounterSection[] = SECTION_ORDER.map((kind) => {
      const entries = e.entries.filter((x) => x.kind === kind && !x.removed);
      const marked = e.marks.has(kind);
      return { kind, status: entries.length > 0 ? "Recorded" : marked ? "NoneReported" : "Empty", entries, reviewedAtUtc: marked ? "2026-10-02T15:10:00Z" : null, reviewedByUserId: marked ? "u1" : null };
    });
    const missing = sections.filter((s) => s.status === "Empty").map((s) => s.kind);
    return {
      id: e.id, patientId: e.patientId, appointmentId: e.appointmentId, encounterAtUtc: e.at, status: e.status, isComplete: missing.length === 0, missingSections: missing, sections,
      addenda: e.addenda.map((a) => ({ id: a.id, text: a.text, createdAtUtc: a.createdAtUtc, createdByUserId: a.createdByUserId })), history: e.events, createdAtUtc: e.at, createdByUserId: "u1", finalizedAtUtc: e.finalizedAtUtc,
      finalizedByUserId: e.status === "Finalized" ? "u1" : null, rowVersion: `e${e.v}`,
    };
  }

  private summary(e: StoredEncounter): EncounterSummary {
    const d = this.view(e);
    return { id: e.id, appointmentId: e.appointmentId, encounterAtUtc: e.at, status: e.status, isComplete: d.isComplete, entryCount: e.entries.filter((x) => !x.removed).length, addendumCount: e.addenda.length };
  }

  private event(e: StoredEncounter, eventType: string, detail: string) {
    e.events.push({ eventType, actorUserId: "u1", occurredAtUtc: new Date(2026, 9, 3, 10, e.events.length).toISOString(), detail });
  }

  private stale = (e: StoredEncounter, body: Record<string, unknown> | null) => body?.rowVersion !== `e${e.v}`;
  private conflict = (id: string) => json(409, { error: "concurrency_conflict", entityType: "Encounter", entityId: id, message: "Changed by someone else." });
  private refuse = (status: number, error: string, message: string, fieldErrors: Record<string, string> = {}) => json(status, { error, message, fieldErrors });

  private fieldErrors(kind: string, b: Record<string, unknown>): Record<string, string> {
    const errors: Record<string, string> = {};
    const s = (k: string) => (typeof b[k] === "string" ? (b[k] as string).trim() : "");
    if (!s("name")) errors.name = "A name is required.";
    if (kind === "Allergy" && s("severity") && !SEVERITIES.includes(s("severity"))) errors.severity = "Severity must be Mild, Moderate or Severe, or left blank when it is not known.";
    if (kind !== "Allergy") { if (s("reaction")) errors.reaction = "A reaction applies to allergies only."; if (s("severity")) errors.severity = "A severity applies to allergies only."; }
    if (kind !== "Medication") { if (s("dose")) errors.dose = "A dose applies to medications only."; if (s("frequency")) errors.frequency = "A frequency applies to medications only."; }
    return errors;
  }

  protected override async handleExtra(path: string, method: string, headers: Record<string, string>, body: Record<string, unknown> | null, u: URL): Promise<Response | null> {
    const isClinical = path.startsWith("/api/encounters") || /^\/api\/patients\/[^/]+\/encounters/.test(path);
    if (!isClinical) return null;
    this.clinicalCalls.push({ method, url: u.pathname + u.search, headers, body });
    for (const [key, queue] of this.injectedClinical) {
      const [m, prefix] = key.split(" ");
      if (m === method && path.startsWith(prefix) && queue.length > 0) return queue.shift()!;
    }
    for (const [key, gate] of this.gates) {
      const [m, prefix] = key.split(" ");
      if (m === method && path.startsWith(prefix)) { this.gates.delete(key); await gate; break; }
    }
    const response = this.route(path, method, headers, body);
    for (const key of this.dropNext) {
      const [m, suffix] = key.split(" ");
      if (m === method && path.endsWith(suffix)) { this.dropNext.delete(key); throw new TypeError("Failed to fetch"); }
    }
    return response;
  }

  private route(path: string, method: string, headers: Record<string, string>, body: Record<string, unknown> | null): Response {
    const parts = path.split("/");
    const key = headers["Idempotency-Key"];

    // ----- a patient's encounters -----
    if (parts[2] === "patients") {
      const patientId = parts[3];
      if (method === "GET") return json(200, [...this.encounters.values()].filter((e) => e.patientId === patientId).sort((a, b) => b.at.localeCompare(a.at)).map((e) => this.summary(e)));
      if (method === "POST") {
        const replay = key ? [...this.encounters.values()].find((e) => e.startKey === key) : undefined;
        if (replay) return json(200, this.view(replay));
        const e = this.addEncounter(patientId, { at: new Date(2026, 9, 3, 9, this.seq).toISOString() });
        e.startKey = key ?? null;
        return json(201, this.view(e));
      }
    }

    // ----- one encounter -----
    const e = this.encounters.get(parts[3]);
    if (!e) return json(404, { error: "encounter_not_found", message: "That encounter was not found." });
    const tail = parts.slice(4).join("/");
    if (method === "GET" && tail === "") return json(200, this.view(e));

    if (tail === "addenda" && method === "POST") {
      if (e.status !== "Finalized") return this.refuse(409, "encounter_not_finalized", "Only a finalized encounter takes an addendum. Change the draft instead.");
      const text = typeof body?.text === "string" ? body.text.trim() : "";
      if (!text) return this.refuse(400, "validation_failed", "Some fields need attention.", { text: "Write the addendum." });
      if (!key) return this.refuse(400, "validation_failed", "Some fields need attention.", { clientKey: "A request key is required so a retry never adds the addendum twice." });
      const replay = e.addenda.find((a) => a.key === key);
      if (replay) return replay.text === text ? json(200, this.view(e)) : this.refuse(409, "idempotency_key_reused", "That request key was already used for a different addendum.");
      e.addenda.push({ id: `a-${++this.seq}`, text, createdAtUtc: new Date(2026, 9, 3, 11, this.seq).toISOString(), createdByUserId: "u1", key });
      this.event(e, "AddendumAdded", "Addendum added.");
      return json(201, this.view(e));
    }

    // every other change: a finalized encounter refuses it, and the caller must hold the current version
    if (method !== "GET") {
      if (typeof body?.rowVersion !== "string" || body.rowVersion === "") return this.refuse(400, "row_version_required", "The version you are working on is required.");
      if (tail === "finalize" && e.status === "Finalized") return json(200, this.view(e));
      if (e.status === "Finalized") return this.refuse(409, "encounter_finalized", "This encounter is finalized and cannot be changed. Add an addendum to correct or extend it.");
      if (this.stale(e, body) && !(tail === "entries" && method === "POST")) return this.conflict(e.id); // an add checks for a retried duplicate first, like the real service
    }

    if (tail === "entries" && method === "POST") {
      const kind = String(body?.kind ?? "");
      const errors = this.fieldErrors(kind, body ?? {});
      if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
      if (e.marks.has(kind)) return this.refuse(409, "section_marked_none_reported", "That section is marked reviewed, none reported. Clear that review before adding an entry.");
      const name = String(body!.name).trim();
      const clean = (k: string) => (typeof body![k] === "string" && (body![k] as string).trim() ? (body![k] as string).trim() : null);
      const twin = e.entries.find((x) => x.kind === kind && !x.removed && x.name.toLowerCase() === name.toLowerCase());
      if (twin) {
        const same = twin.detail === clean("detail") && twin.reaction === clean("reaction") && twin.severity === clean("severity") && twin.dose === clean("dose") && twin.frequency === clean("frequency");
        return same ? json(200, this.view(e)) : this.refuse(409, "duplicate_entry", `'${name}' is already listed. Change that entry instead.`); // a retried add is a quiet repeat
      }
      if (this.stale(e, body)) return this.conflict(e.id);
      e.entries.push(this.makeEntry({ kind, name, detail: clean("detail"), reaction: clean("reaction"), severity: clean("severity"), dose: clean("dose"), frequency: clean("frequency") }));
      e.v++; this.event(e, "EntryAdded", "Entry added.");
      return json(200, this.view(e));
    }
    if (parts[4] === "entries" && parts[5]) {
      const entry = e.entries.find((x) => x.id === parts[5]);
      if (!entry) return this.refuse(404, "entry_not_found", "That entry was not found on this encounter.");
      if (parts[6] === "remove" && method === "POST") {
        if (!entry.removed) { entry.removed = true; e.v++; this.event(e, "EntryRemoved", "Entry removed."); }
        return json(200, this.view(e));
      }
      if (method === "PUT") {
        const errors = this.fieldErrors(entry.kind, body ?? {});
        if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
        const clean = (k: string) => (typeof body![k] === "string" && (body![k] as string).trim() ? (body![k] as string).trim() : null);
        Object.assign(entry, { name: String(body!.name).trim(), detail: clean("detail"), reaction: clean("reaction"), severity: clean("severity"), dose: clean("dose"), frequency: clean("frequency"), updatedAtUtc: "2026-10-03T10:00:00Z" });
        e.v++; this.event(e, "EntryChanged", "Entry changed.");
        return json(200, this.view(e));
      }
    }
    if (parts[4] === "sections" && parts[5] && method === "POST") {
      const kind = parts[5];
      if (!SECTION_ORDER.includes(kind as never)) return this.refuse(400, "validation_failed", "That section does not exist.", { kind: "Choose a section." });
      if (parts[6] === "none-reported") {
        if (e.entries.some((x) => x.kind === kind && !x.removed)) return this.refuse(409, "section_has_entries", "That section already has entries, so it cannot be marked none reported. Remove them first.");
        if (!e.marks.has(kind)) { e.marks.add(kind); e.v++; this.event(e, "SectionMarked", "Section reviewed, none reported."); }
        return json(200, this.view(e));
      }
      if (parts[6] === "clear-review") {
        if (e.marks.delete(kind)) { e.v++; this.event(e, "SectionUnmarked", "Section review cleared."); }
        return json(200, this.view(e));
      }
    }
    if (tail === "finalize" && method === "POST") {
      const missing = this.view(e).missingSections;
      if (missing.length > 0) return this.refuse(409, "documentation_incomplete", `The documentation is not complete: ${missing.join(", ")} still need attention.`, Object.fromEntries(missing.map((k) => [k, "Record at least one entry or mark it reviewed, none reported."])));
      e.status = "Finalized"; e.finalizedAtUtc = "2026-10-03T12:00:00Z"; e.v++; this.event(e, "Finalized", "Encounter finalized.");
      return json(200, this.view(e));
    }
    return json(404, { error: "not_found", message: "Not found." });
  }
}
