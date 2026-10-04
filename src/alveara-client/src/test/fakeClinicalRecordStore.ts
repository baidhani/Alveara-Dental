import { json } from "./fakePatientServer";
import type { ClinicalRecord, ItemHistory, ItemStatus, ItemVersion, RecordItem, RecordSection, SectionStatus } from "../services/clinicalRecordApi";
import { statusesFor } from "../services/clinicalRecordApi";
import type { NoteTemplate } from "../services/clinicalNotesApi";
import { NOTE_SECTIONS } from "../services/clinicalNotesApi";
import { SECTION_ORDER } from "../services/clinicalApi";

/**
 * ALV-005-C01: an in-memory stand-in for the longitudinal clinical record and note template APIs (Controllers/ClinicalRecordController and the template half of
 * ClinicalNotesController). It models the response SHAPES, the item row-version check (a stale write is the shared 409 concurrency conflict), the derived section statuses
 * (reviewed / needs review / none known / unknown / not reviewed), the rules a UI has to render (a reason when removing, a status that fits the kind, no invented items)
 * and the template rules (unique name, sections). The real rules are proven by the backend tests and the real-backend walkthrough; this exists so the UI's handling of each
 * outcome can be tested in isolation.
 */
interface StoredItem {
  id: string; patientId: string; kind: string; name: string; detail: string | null; reaction: string | null; severity: string | null; dose: string | null; frequency: string | null;
  status: ItemStatus; createdAt: string; createdByName: string; updatedAt: string | null; updatedByName: string | null; v: number; removed: boolean; versions: ItemVersion[];
}
interface StoredReview { state: "Reviewed" | "NoneKnown" | "Unknown"; at: string; by: string }
interface StoredTemplate { id: string; name: string; description: string | null; isActive: boolean; sections: NoteTemplate["sections"]; v: number; updatedAt: string; updatedBy: string }

const SEVERITIES = ["Mild", "Moderate", "Severe"];
const ACTOR = "Dr. Okafor";

export class FakeClinicalRecordStore {
  items = new Map<string, StoredItem>();
  reviews = new Map<string, StoredReview>();
  templates = new Map<string, StoredTemplate>();
  private seq = 0;
  private clock = 0;
  /** Whether the caller holds ManageClinicalTemplates - set by the owning server from its permission list. */
  canConfigure = () => true;

  private now() { return new Date(Date.UTC(2026, 9, 3, 12, 0, this.clock++)).toISOString(); }

  // ---------- seeding ----------
  addItem(patientId: string, over: Partial<StoredItem> = {}): StoredItem {
    const at = over.createdAt ?? "2026-09-01T10:00:00Z";
    const item: StoredItem = {
      id: over.id ?? `it-${++this.seq}`, patientId, kind: over.kind ?? "Allergy", name: over.name ?? "Penicillin", detail: over.detail ?? null, reaction: over.reaction ?? null,
      severity: over.severity ?? null, dose: over.dose ?? null, frequency: over.frequency ?? null, status: over.status ?? "Active", createdAt: at, createdByName: over.createdByName ?? ACTOR,
      updatedAt: over.updatedAt ?? null, updatedByName: over.updatedByName ?? null, v: 1, removed: false, versions: [],
    };
    item.versions.push(this.version(item, "Added", null));
    this.items.set(item.id, item);
    return item;
  }

  review(patientId: string, kind: string, state: StoredReview["state"], at = "2026-09-15T10:00:00Z", by = "Hana Hygienist") { this.reviews.set(`${patientId}|${kind}`, { state, at, by }); }

  addTemplate(over: Partial<StoredTemplate> = {}): StoredTemplate {
    const t: StoredTemplate = {
      id: over.id ?? `tpl-${++this.seq}`, name: over.name ?? "SOAP note", description: over.description ?? null, isActive: over.isActive ?? true,
      sections: over.sections ?? [{ section: "Subjective", required: true, starterText: "Chief complaint:" }, { section: "Plan", required: true, starterText: null }], v: 1, updatedAt: this.now(), updatedBy: ACTOR,
    };
    this.templates.set(t.id, t);
    return t;
  }

  // ---------- views ----------
  private version(i: StoredItem, changeType: ItemVersion["changeType"], reason: string | null): ItemVersion {
    return { versionNumber: i.versions.length + 1, changeType, name: i.name, detail: i.detail, reaction: i.reaction, severity: i.severity, dose: i.dose, frequency: i.frequency, status: i.status, reason, encounterId: null, actorName: ACTOR, occurredAtUtc: this.now() };
  }

  private itemView(i: StoredItem): RecordItem {
    return { id: i.id, kind: i.kind, name: i.name, detail: i.detail, reaction: i.reaction, severity: i.severity, dose: i.dose, frequency: i.frequency, status: i.status, createdAtUtc: i.createdAt, createdByName: i.createdByName, updatedAtUtc: i.updatedAt, updatedByName: i.updatedByName, rowVersion: `i${i.v}` };
  }

  private record(patientId: string): ClinicalRecord {
    const sections: RecordSection[] = SECTION_ORDER.map((kind) => {
      const live = [...this.items.values()].filter((i) => i.patientId === patientId && i.kind === kind && !i.removed);
      const review = this.reviews.get(`${patientId}|${kind}`);
      let status: SectionStatus = "NotReviewed";
      if (live.length > 0) {
        const newest = live.map((i) => i.updatedAt ?? i.createdAt).sort().at(-1)!;
        status = review?.state === "Reviewed" && review.at >= newest ? "Reviewed" : "NeedsReview";
      } else if (review?.state === "NoneKnown") status = "NoneKnown";
      else if (review?.state === "Unknown") status = "Unknown";
      return { kind, status, items: live.map((i) => this.itemView(i)), reviewedAtUtc: review?.at ?? null, reviewedByName: review?.by ?? null };
    });
    return { patientId, sections, timeline: [] };
  }

  private templateView(t: StoredTemplate): NoteTemplate {
    return { id: t.id, name: t.name, description: t.description, isActive: t.isActive, sections: t.sections, updatedAtUtc: t.updatedAt, updatedByName: t.updatedBy, rowVersion: `t${t.v}` };
  }

  // ---------- routing ----------
  private refuse = (status: number, error: string, message: string, fieldErrors: Record<string, string> = {}) => json(status, { error, message, fieldErrors });
  private conflict = (entityType: string, id: string) => json(409, { error: "concurrency_conflict", entityType, entityId: id, message: "Changed by someone else." });

  private fields(kind: string, b: Record<string, unknown>): Record<string, string> {
    const errors: Record<string, string> = {};
    const s = (k: string) => (typeof b[k] === "string" ? (b[k] as string).trim() : "");
    if (!s("name")) errors.name = "A name is required.";
    if (kind === "Allergy" && s("severity") && !SEVERITIES.includes(s("severity"))) errors.severity = "Severity must be Mild, Moderate or Severe, or left blank when it is not known.";
    if (kind !== "Allergy") { if (s("reaction")) errors.reaction = "A reaction applies to allergies only."; if (s("severity")) errors.severity = "A severity applies to allergies only."; }
    if (kind !== "Medication") { if (s("dose")) errors.dose = "A dose applies to medications only."; if (s("frequency")) errors.frequency = "A frequency applies to medications only."; }
    return errors;
  }

  /** Answers a record or template request, or null when the path is not one of them. */
  route(path: string, method: string, body: Record<string, unknown> | null, u: URL): Response | null {
    const parts = path.split("/");
    const clean = (k: string) => (typeof body?.[k] === "string" && (body[k] as string).trim() ? (body[k] as string).trim() : null);

    if (parts[2] === "patients" && parts[4] === "clinical-record") {
      const patientId = parts[3];
      if (method === "GET" && parts.length === 5) return json(200, this.record(patientId));
      if (parts[5] === "items" && method === "POST") {
        const kind = String(body?.kind ?? "");
        if (!SECTION_ORDER.includes(kind as never)) return this.refuse(400, "validation_failed", "That section does not exist.", { kind: "Choose a section." });
        const errors = this.fields(kind, body ?? {});
        if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
        const name = String(body!.name).trim();
        const twin = [...this.items.values()].find((i) => i.patientId === patientId && i.kind === kind && !i.removed && i.name.toLowerCase() === name.toLowerCase());
        if (twin) {
          const same = twin.detail === clean("detail") && twin.reaction === clean("reaction") && twin.severity === clean("severity") && twin.dose === clean("dose") && twin.frequency === clean("frequency");
          return same ? json(200, this.record(patientId)) : this.refuse(409, "duplicate_item", `'${name}' is already listed (status ${twin.status}). Change that item instead.`);
        }
        const review = this.reviews.get(`${patientId}|${kind}`);
        if (review && review.state !== "Reviewed") this.reviews.delete(`${patientId}|${kind}`);
        this.addItem(patientId, { kind, name, detail: clean("detail"), reaction: clean("reaction"), severity: clean("severity"), dose: clean("dose"), frequency: clean("frequency"), createdAt: this.now() });
        return json(200, this.record(patientId));
      }
      if (parts[5] === "sections" && parts[7] === "review" && method === "PUT") {
        const kind = parts[6];
        const state = String(body?.state ?? "");
        if (!SECTION_ORDER.includes(kind as never) || !["Reviewed", "NoneKnown", "Unknown", "NotReviewed"].includes(state)) return this.refuse(400, "validation_failed", "That review state does not exist.", { state: "Choose a state." });
        const live = [...this.items.values()].filter((i) => i.patientId === patientId && i.kind === kind && !i.removed).length;
        if (state === "NotReviewed") { this.reviews.delete(`${patientId}|${kind}`); return json(200, this.record(patientId)); }
        if (state === "Reviewed" && live === 0) return this.refuse(409, "section_empty", "Nothing is listed to confirm. Mark it none known or unknown instead.");
        if (state !== "Reviewed" && live > 0) return this.refuse(409, "section_has_items", "That section already has items, so it cannot be marked none known or unknown.");
        this.review(patientId, kind, state as StoredReview["state"], this.now(), ACTOR);
        return json(200, this.record(patientId));
      }
    }

    if (parts[2] === "clinical-record" && parts[3] === "items" && parts[4]) {
      const item = this.items.get(parts[4]);
      if (!item) return this.refuse(404, "item_not_found", "That item was not found.");
      if (method === "GET" && parts[5] === "history") {
        const history: ItemHistory = { itemId: item.id, kind: item.kind, versions: item.versions };
        return json(200, history);
      }
      if (method === "GET") return json(404, { error: "not_found", message: "Not found." });
      if (typeof body?.rowVersion !== "string" || body.rowVersion === "") return this.refuse(400, "row_version_required", "The version you are working on is required.");
      if (item.removed) return this.refuse(409, "item_removed", "That item was removed as entered in error.");
      if (body.rowVersion !== `i${item.v}`) return this.conflict("ClinicalRecordItem", item.id);
      const touch = (type: ItemVersion["changeType"], reason: string | null) => { item.v++; item.updatedAt = this.now(); item.updatedByName = ACTOR; item.versions.push(this.version(item, type, reason)); };
      if (method === "PUT" && parts.length === 5) {
        const errors = this.fields(item.kind, body);
        if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
        Object.assign(item, { name: String(body.name).trim(), detail: clean("detail"), reaction: clean("reaction"), severity: clean("severity"), dose: clean("dose"), frequency: clean("frequency") });
        touch("Changed", clean("reason"));
        return json(200, this.record(item.patientId));
      }
      if (parts[5] === "status" && method === "POST") {
        const status = String(body.status ?? "") as ItemStatus;
        if (!statusesFor(item.kind).includes(status)) return this.refuse(400, "validation_failed", "That status does not apply to this kind of item.", { status: "Choose a status that fits." });
        if (item.status !== status) { item.status = status; touch("StatusChanged", clean("reason")); }
        return json(200, this.record(item.patientId));
      }
      if (parts[5] === "remove" && method === "POST") {
        const reason = clean("reason");
        if (!reason) return this.refuse(400, "validation_failed", "Some fields need attention.", { reason: "Say why it is being removed." });
        item.removed = true;
        touch("RemovedInError", reason);
        return json(200, this.record(item.patientId));
      }
    }

    if (parts[2] === "clinical" && parts[3] === "templates") {
      if (method === "GET" && !parts[4]) {
        const includeInactive = u.searchParams.get("includeInactive") === "true" && this.canConfigure();
        return json(200, [...this.templates.values()].filter((t) => includeInactive || t.isActive).sort((a, b) => a.name.localeCompare(b.name)).map((t) => this.templateView(t)));
      }
      if (method === "POST" && !parts[4]) {
        const errors = this.templateErrors(body);
        if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
        const name = String(body!.name).trim();
        if ([...this.templates.values()].some((t) => t.name.toLowerCase() === name.toLowerCase())) return this.refuse(409, "template_name_taken", `A template named '${name}' already exists.`, { name: "That name is already used by another template." });
        const t = this.addTemplate({ name, description: clean("description"), sections: this.sectionsOf(body) });
        return json(201, this.templateView(t));
      }
      const t = this.templates.get(parts[4]);
      if (!t) return this.refuse(404, "template_not_found", "That template was not found.");
      if (typeof body?.rowVersion !== "string" || body.rowVersion === "") return this.refuse(400, "row_version_required", "The version you are working on is required.");
      if (body.rowVersion !== `t${t.v}`) return this.conflict("NoteTemplate", t.id);
      if (method === "PUT") {
        const errors = this.templateErrors(body);
        if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
        const name = String(body.name).trim();
        if ([...this.templates.values()].some((o) => o.id !== t.id && o.name.toLowerCase() === name.toLowerCase())) return this.refuse(409, "template_name_taken", `A template named '${name}' already exists.`, { name: "That name is already used by another template." });
        Object.assign(t, { name, description: clean("description"), sections: this.sectionsOf(body), v: t.v + 1, updatedAt: this.now() });
        return json(200, this.templateView(t));
      }
      if (method === "POST" && parts[5] === "active") {
        if (t.isActive !== Boolean(body.active)) { t.isActive = Boolean(body.active); t.v++; }
        return json(200, this.templateView(t));
      }
    }
    return null;
  }

  private sectionsOf(body: Record<string, unknown> | null): NoteTemplate["sections"] {
    return ((body?.sections as { section: string; required: boolean; starterText?: string }[]) ?? []).map((s) => ({ section: s.section, required: !!s.required, starterText: s.starterText?.trim() ? s.starterText.trim() : null }));
  }

  private templateErrors(body: Record<string, unknown> | null): Record<string, string> {
    const errors: Record<string, string> = {};
    if (typeof body?.name !== "string" || body.name.trim() === "") errors.name = "A name is required.";
    const sections = (body?.sections as { section: string }[] | undefined) ?? [];
    if (sections.length === 0) errors.sections = "Choose at least one note section.";
    else if (sections.some((s) => !(NOTE_SECTIONS as readonly string[]).includes(s.section))) errors.sections = "A section in the template does not exist.";
    return errors;
  }
}
