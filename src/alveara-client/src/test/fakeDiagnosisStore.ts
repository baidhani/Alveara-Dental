import { json } from "./fakePatientServer";
import type { Diagnosis, DiagnosisLink, DiagnosisProblem, DiagnosisVersion } from "../services/diagnosisApi";
import { checkStructure } from "../pages/diagnosis/diagnosisStructureRules";
import { NOTES_MAX, checkDiagnosis, normalizeNotes } from "../pages/diagnosis/diagnosisRules";

/**
 * STORY-013: an in-memory stand-in for the diagnosis API (Controllers/DiagnosesController): the response SHAPES, the server's validation (every problem listed together, nothing partial saved), the
 * encounter-must-be-this-patient's rule, the idempotent record (the same key and entry return the diagnosis already saved; the same key with a different entry is a 409), corrections in which the treatment-plan
 * forward reference is kept unless explicitly replaced or cleared, withdrawal with a reason, a row version (a stale change is the shared 409 conflict) and the append-only history. The real rules are proven
 * by the backend tests and the real-backend walkthrough; this exists so the UI's handling of each outcome can be tested in isolation.
 *
 * ALV-013-C01: also the structure (coding, source, region), amendment, resolve and reactivate, and links to findings and periodontal charts of the same patient. The treatment-plan reference can never be
 * marked as anything but Unresolved.
 */
const ACTOR = "Dr. Okafor";
const state = (reference: string | null) => (reference === null ? null : "Unresolved");

interface Stored extends Omit<Diagnosis, "rowVersion" | "treatmentPlanReferenceState" | "encounterAtUtc" | "links"> { key: string; v: number; versions: DiagnosisVersion[]; links: DiagnosisLink[] }

export class FakeDiagnosisStore {
  diagnoses: Stored[] = [];
  /** Set by the fake clinical server: the patient and time of an encounter, or undefined when there is none. */
  encounterOf: (id: string) => { patientId: string; at: string } | undefined = () => undefined;
  /** Set by the fake clinical server: what a finding or periodontal chart of this patient is called, or undefined when there is none (or it is another patient's). */
  linkTargetOf: (type: string, id: string, patientId: string) => string | undefined = () => undefined;
  private seq = 0;
  private clock = 0;
  private now() { return new Date(Date.UTC(2026, 9, 5, 9, 0, this.clock++)).toISOString(); }

  /** A diagnosis made earlier (for list and history tests). */
  add(patientId: string, encounterId: string, over: Partial<Pick<Stored, "label" | "toothKey" | "notes" | "treatmentPlanReference" | "status" | "codingSystem" | "code" | "source" | "sourceNote" | "regionKey">> = {}): Diagnosis {
    const at = this.now();
    const d: Stored = {
      id: `dg-${++this.seq}`, patientId, encounterId, key: `seed-${this.seq}`, label: over.label ?? "Chronic periodontitis", toothKey: over.toothKey ?? null, notes: over.notes ?? null,
      treatmentPlanReference: over.treatmentPlanReference ?? null, status: over.status ?? "Active", recordedByName: ACTOR, recordedAtUtc: at, updatedByName: null, updatedAtUtc: null,
      withdrawnByName: null, withdrawnAtUtc: null, withdrawnReason: null, v: 1, versions: [], links: [],
      codingSystem: over.codingSystem ?? null, code: over.code ?? null, source: over.source ?? "Manual", sourceNote: over.sourceNote ?? null, regionKey: over.regionKey ?? null,
    };
    d.versions.push(this.version(d, 1, "Recorded", null));
    this.diagnoses.push(d);
    return this.view(d);
  }

  /** A link already made from a diagnosis (as the server would hold it). */
  addLink(id: string, linkType: DiagnosisLink["linkType"], targetId: string, summary: string) {
    this.diagnoses.find((d) => d.id === id)!.links.push({ linkType, targetId, summary, linkedByName: ACTOR, linkedAtUtc: this.now() });
  }

  /** A stand-in for someone else changing a diagnosis behind the screen's back: the version moves, so the next change from the screen is stale. */
  touch(id: string) { this.diagnoses.find((d) => d.id === id)!.v++; }

  private version(d: Stored, n: number, type: DiagnosisVersion["changeType"], reason: string | null): DiagnosisVersion {
    return { versionNumber: n, changeType: type, label: d.label, toothKey: d.toothKey, notes: d.notes, treatmentPlanReference: d.treatmentPlanReference, treatmentPlanReferenceState: state(d.treatmentPlanReference), status: d.status, reason, actorName: ACTOR, occurredAtUtc: this.now(),
      codingSystem: d.codingSystem ?? null, code: d.code ?? null, source: d.source ?? "Manual", sourceNote: d.sourceNote ?? null, regionKey: d.regionKey ?? null };
  }

  private view({ key: _k, v, versions: _v, ...d }: Stored): Diagnosis {
    return { ...d, links: [...d.links], encounterAtUtc: this.encounterOf(d.encounterId)?.at ?? "2026-10-05T09:00:00Z", treatmentPlanReferenceState: state(d.treatmentPlanReference), rowVersion: `v${v}` };
  }

  private refuse = (status: number, error: string, message: string, problems: DiagnosisProblem[] = []) => json(status, { error, message, problems });
  private conflict = (id: string) => json(409, { error: "concurrency_conflict", entityType: "Diagnosis", entityId: id, message: "Changed by someone else." });

  route(path: string, method: string, body: Record<string, unknown> | null, url?: URL): Response | null {
    const parts = path.split("/");
    if (parts[2] === "patients" && parts[4] === "diagnoses") {
      const patientId = parts[3];
      if (method === "GET") {
        const encounterId = url?.searchParams.get("encounterId"), withdrawn = url?.searchParams.get("includeWithdrawn") === "true";
        const rows = this.diagnoses.filter((d) => d.patientId === patientId && (!encounterId || d.encounterId === encounterId) && (withdrawn || d.status !== "Withdrawn")).reverse();
        return json(200, { patientId, diagnoses: rows.map((d) => this.view(d)) });
      }
      return method === "POST" ? this.record(patientId, body) : null;
    }
    if (parts[2] !== "diagnoses") return null;
    const d = this.diagnoses.find((x) => x.id === parts[3]);
    if (!d) return this.refuse(404, "diagnosis_not_found", "That diagnosis was not found.");
    if (method === "GET" && parts.length === 4) return json(200, this.view(d));
    if (method === "GET" && parts[4] === "history") return json(200, { diagnosisId: d.id, versions: d.versions });
    if (method === "POST" && parts[4] === "correct") return this.correct(d, body ?? {});
    if (method === "POST" && parts[4] === "withdraw") return this.withdraw(d, body ?? {});
    if (method === "POST" && parts[4] === "amend") return this.amend(d, body ?? {});
    if (method === "POST" && parts[4] === "resolve") return this.setStatus(d, body ?? {}, "Resolved", "Resolved", "resolved");
    if (method === "POST" && parts[4] === "reactivate") return this.setStatus(d, body ?? {}, "Active", "Reactivated", "reactivated");
    if (method === "POST" && parts[4] === "links") return this.link(d, body ?? {});
    return null;
  }

  private record(patientId: string, body: Record<string, unknown> | null): Response {
    const key = typeof body?.idempotencyKey === "string" ? body.idempotencyKey.trim() : "";
    const str = (k: string) => (typeof body?.[k] === "string" ? (body[k] as string) : null);
    const check = checkDiagnosis({ encounterId: str("encounterId"), label: str("label"), toothKey: str("toothKey"), notes: str("notes"), treatmentPlanReference: str("treatmentPlanReference") });
    const structure = checkStructure({ toothKey: str("toothKey"), codingSystem: str("codingSystem"), code: str("code"), source: str("source"), sourceNote: str("sourceNote"), regionKey: str("regionKey") });
    const problems = [...check.problems, ...structure.problems, ...this.stateProblems(body), ...(key === "" || key.length > 64 ? [{ field: "idempotencyKey", code: "invalid", message: "Send a key of 1 to 64 characters with each save, so a retry cannot create a second diagnosis." }] : [])];
    if (problems.length > 0) return this.refuse(400, "validation_failed", "The diagnosis has entries that need correcting. Nothing was saved.", problems);
    const v = check.value!;
    const s = structure.value!;
    const encounter = this.encounterOf(v.encounterId);
    if (!encounter || encounter.patientId !== patientId) return this.refuse(404, "encounter_not_found", "That encounter was not found for this patient. Choose one of this patient's encounters.", [{ field: "encounterId", code: "not_found", message: "Choose one of this patient's encounters." }]);
    const existing = this.diagnoses.find((d) => d.patientId === patientId && d.key === key);
    if (existing) {
      const first = existing.versions[0];
      const same = existing.encounterId === v.encounterId && first.label === v.label && first.toothKey === v.toothKey && first.notes === v.notes && first.treatmentPlanReference === v.treatmentPlanReference
        && (first.codingSystem ?? null) === s.codingSystem && (first.code ?? null) === s.code && (first.source ?? "Manual") === s.source && (first.sourceNote ?? null) === s.sourceNote && (first.regionKey ?? null) === s.regionKey;
      return same ? json(200, this.view(existing)) : this.refuse(409, "idempotency_key_reused", "That save key was already used for a different diagnosis. Nothing was changed; save again to record this one as a new diagnosis.");
    }
    const at = this.now();
    const d: Stored = {
      id: `dg-${++this.seq}`, patientId, encounterId: v.encounterId, key, label: v.label, toothKey: v.toothKey, notes: v.notes, treatmentPlanReference: v.treatmentPlanReference, status: "Active",
      recordedByName: ACTOR, recordedAtUtc: at, updatedByName: null, updatedAtUtc: null, withdrawnByName: null, withdrawnAtUtc: null, withdrawnReason: null, v: 1, versions: [], links: [],
      codingSystem: s.codingSystem, code: s.code, source: s.source, sourceNote: s.sourceNote, regionKey: s.regionKey,
    };
    d.versions.push(this.version(d, 1, "Recorded", null));
    this.diagnoses.push(d);
    return json(200, this.view(d));
  }

  private versionProblem(d: Stored, body: Record<string, unknown>): Response | null {
    if (typeof body.rowVersion !== "string" || body.rowVersion === "") return this.refuse(400, "row_version_required", "The version of the diagnosis you are changing is required, so a change made by someone else is never overwritten. Reload and try again.");
    return body.rowVersion === `v${d.v}` ? null : this.conflict(d.id);
  }

  private reasonOf(body: Record<string, unknown>, problems: DiagnosisProblem[]): string | null {
    const r = typeof body.reason === "string" ? normalizeNotes(body.reason) : "";
    if (r === "") { problems.push({ field: "reason", code: "required", message: "Say why." }); return null; }
    if (r.length > 500) { problems.push({ field: "reason", code: "too_long", message: `The reason can be at most 500 characters; this one has ${r.length}.` }); return null; }
    return r;
  }

  private correct(d: Stored, body: Record<string, unknown>): Response {
    const stale = this.versionProblem(d, body);
    if (stale) return stale;
    if (d.status === "Withdrawn") return this.refuse(409, "diagnosis_withdrawn", "This diagnosis was withdrawn, so it cannot be corrected. Record a new diagnosis if one is needed.");
    const problems: DiagnosisProblem[] = [];
    const reason = this.reasonOf(body, problems);
    const clear = body.clearTreatmentPlanReference === true;
    const given = typeof body.treatmentPlanReference === "string" ? body.treatmentPlanReference : null;
    if (clear && given !== null) problems.push({ field: "treatmentPlanReference", code: "conflict", message: "Either replace the treatment-plan reference or clear it, not both." });
    const str = (k: string) => (typeof body[k] === "string" ? (body[k] as string) : null);
    const proposed = clear ? null : given ?? d.treatmentPlanReference;
    const check = checkDiagnosis({ encounterId: d.encounterId, label: str("label"), toothKey: str("toothKey"), notes: str("notes"), treatmentPlanReference: proposed });
    problems.push(...check.problems, ...checkStructure({ toothKey: str("toothKey"), codingSystem: d.codingSystem ?? null, code: d.code ?? null, source: d.source && d.source !== "Manual" ? d.source : null, sourceNote: d.sourceNote ?? null, regionKey: d.regionKey ?? null }).problems, ...this.stateProblems(body));
    if (problems.length > 0) return this.refuse(400, "validation_failed", "The correction has entries that need correcting. Nothing was saved.", problems);
    const v = check.value!;
    if (v.label === d.label && v.toothKey === d.toothKey && v.notes === d.notes && v.treatmentPlanReference === d.treatmentPlanReference) return json(200, this.view(d));
    Object.assign(d, { label: v.label, toothKey: v.toothKey, notes: v.notes, treatmentPlanReference: v.treatmentPlanReference, updatedByName: ACTOR, updatedAtUtc: this.now() });
    d.v++;
    d.versions.push(this.version(d, d.versions.length + 1, "Corrected", reason));
    return json(200, this.view(d));
  }

  private withdraw(d: Stored, body: Record<string, unknown>): Response {
    const problems: DiagnosisProblem[] = [];
    const reason = this.reasonOf(body, problems);
    if (problems.length > 0) return this.refuse(400, "reason_required", "Say why this diagnosis is being withdrawn. Nothing was changed.", problems);
    if (d.status === "Withdrawn") return json(200, this.view(d));
    const stale = this.versionProblem(d, body);
    if (stale) return stale;
    const at = this.now();
    Object.assign(d, { status: "Withdrawn", withdrawnByName: ACTOR, withdrawnAtUtc: at, withdrawnReason: reason, updatedByName: ACTOR, updatedAtUtc: at });
    d.v++;
    d.versions.push(this.version(d, d.versions.length + 1, "Withdrawn", reason));
    return json(200, this.view(d));
  }
  /** A request to mark the treatment-plan reference as anything but Unresolved is refused: there are no plans to resolve it against. */
  private stateProblems(body: Record<string, unknown> | null): DiagnosisProblem[] {
    const s = body?.treatmentPlanReferenceState;
    return typeof s === "string" && s !== "Unresolved"
      ? [{ field: "treatmentPlanReferenceState", code: "not_supported", message: "A treatment-plan reference cannot be resolved or validated yet: treatment plans do not exist, so it stays unresolved." }] : [];
  }

  private amend(d: Stored, body: Record<string, unknown>): Response {
    const problems: DiagnosisProblem[] = [];
    const reason = this.reasonOf(body, problems);
    const stale = this.versionProblem(d, body);
    if (stale) return stale;
    if (d.status === "Withdrawn") return this.refuse(409, "diagnosis_withdrawn", "This diagnosis was withdrawn, so it cannot be amended. Record a new diagnosis if one is needed.");
    const str = (k: string) => (typeof body[k] === "string" ? (body[k] as string) : null);
    const structure = checkStructure({ toothKey: str("toothKey"), codingSystem: str("codingSystem"), code: str("code"), source: str("source"), sourceNote: str("sourceNote"), regionKey: str("regionKey") });
    problems.push(...structure.problems, ...this.stateProblems(body));
    if (problems.length > 0) return this.refuse(400, "validation_failed", "The amendment has entries that need correcting. Nothing was saved.", problems);
    const s = structure.value!;
    const tooth = str("toothKey");
    if (tooth === d.toothKey && s.regionKey === (d.regionKey ?? null) && s.codingSystem === (d.codingSystem ?? null) && s.code === (d.code ?? null) && s.source === (d.source ?? "Manual") && s.sourceNote === (d.sourceNote ?? null)) return json(200, this.view(d));
    Object.assign(d, { toothKey: tooth, regionKey: s.regionKey, codingSystem: s.codingSystem, code: s.code, source: s.source, sourceNote: s.sourceNote, updatedByName: ACTOR, updatedAtUtc: this.now() });
    d.v++;
    d.versions.push(this.version(d, d.versions.length + 1, "Amended", reason));
    return json(200, this.view(d));
  }

  private setStatus(d: Stored, body: Record<string, unknown>, status: "Active" | "Resolved", type: "Resolved" | "Reactivated", word: string): Response {
    const problems: DiagnosisProblem[] = [];
    const reason = this.reasonOf(body, problems);
    if (problems.length > 0) return this.refuse(400, "reason_required", `Say why this diagnosis is being ${word}. Nothing was changed.`, problems);
    if (d.status === "Withdrawn") return this.refuse(409, "diagnosis_withdrawn", `This diagnosis was withdrawn, so it cannot be ${word}. Record a new diagnosis if one is needed.`);
    if (d.status === status) return json(200, this.view(d));
    const stale = this.versionProblem(d, body);
    if (stale) return stale;
    Object.assign(d, { status, updatedByName: ACTOR, updatedAtUtc: this.now() });
    d.v++;
    d.versions.push(this.version(d, d.versions.length + 1, type, reason));
    return json(200, this.view(d));
  }

  private link(d: Stored, body: Record<string, unknown>): Response {
    const type = typeof body.linkType === "string" ? body.linkType : null;
    const target = typeof body.targetId === "string" ? body.targetId : null;
    const problems: DiagnosisProblem[] = [];
    if (type !== "Finding" && type !== "PerioExam") problems.push({ field: "linkType", code: "unsupported_link_type", message: "Link a diagnosis to one of Finding, PerioExam." });
    if (!target) problems.push({ field: "targetId", code: "required", message: "Choose the record to link." });
    if (problems.length > 0) return this.refuse(400, "validation_failed", "The link has entries that need correcting. Nothing was saved.", problems);
    if (d.status === "Withdrawn") return this.refuse(409, "diagnosis_withdrawn", "This diagnosis was withdrawn, so it cannot be linked. Record a new diagnosis if one is needed.");
    const summary = this.linkTargetOf(type!, target!, d.patientId);
    if (summary === undefined) return this.refuse(404, "link_target_not_found", "That record was not found for this patient. Choose one of this patient's records.", [{ field: "targetId", code: "not_found", message: "Choose one of this patient's records." }]);
    if (!d.links.some((l) => l.linkType === type && l.targetId === target)) d.links.push({ linkType: type as DiagnosisLink["linkType"], targetId: target!, summary, linkedByName: ACTOR, linkedAtUtc: this.now() });
    return json(200, this.view(d));
  }
}

export { NOTES_MAX };
