import { ALL_KEYS, isPrimary, surfacesFor } from "../pages/odontogram/toothNumbering";
import { CONDITION_DENTITIONS, CONDITION_SCOPES, FINDING_STATES, LINK_TYPES, TOOTH_EFFECTS } from "../services/odontogramApi";
import type { Chart, ConditionType, ConditionTypeEvent, Finding, FindingLink, FindingVersion, ToothEvent } from "../services/odontogramApi";
import { json } from "./fakePatientServer";

/**
 * STORY-006 / ALV-006-C01: an in-memory stand-in for the odontogram API (Controllers/OdontogramController). It models the response SHAPES, the row-version check (a stale write is the shared
 * 409 concurrency conflict), and the rules a UI has to render: a tooth must be one of the 52, the condition must exist (spelled exactly) and be active, apply to the tooth's dentition and
 * fit the surface rules of its scope, a tooth recorded as missing takes nothing but an implant, a state only moves forward, a withdrawal needs a reason, repeats are quiet; the condition
 * catalogue can be extended and retired; a tooth's timeline reads every finding on it; links are append-only. The real rules are proven by the backend tests and the real-backend walkthrough;
 * this exists so the UI's handling of each outcome can be tested in isolation. A patient with nothing seeded has an EMPTY chart.
 */
const ACTOR = "Dr. Okafor";
const FORWARD: Record<string, string[]> = { Diagnosed: ["Planned", "Completed"], Planned: ["Completed"] };
const CODE_SHAPE = /^[A-Z][A-Za-z0-9]{1,31}$/;

const SEED: Omit<ConditionType, "id" | "isActive" | "createdByName" | "createdAtUtc" | "rowVersion">[] = [
  { code: "Caries", label: "Caries", scope: "Surface", appliesTo: "Both", toothEffect: "None" },
  { code: "Restoration", label: "Restoration", scope: "Surface", appliesTo: "Both", toothEffect: "None" },
  { code: "Crown", label: "Crown", scope: "WholeTooth", appliesTo: "Both", toothEffect: "None" },
  { code: "Missing", label: "Missing tooth", scope: "WholeTooth", appliesTo: "Both", toothEffect: "Absent" },
  { code: "Implant", label: "Implant", scope: "WholeTooth", appliesTo: "Permanent", toothEffect: "Replacement" },
  { code: "RootCanal", label: "Root canal", scope: "WholeTooth", appliesTo: "Both", toothEffect: "None" },
];

interface StoredType extends Omit<ConditionType, "rowVersion"> { v: number; events: ConditionTypeEvent[] }
interface StoredFinding extends Omit<Finding, "rowVersion" | "links" | "conditionLabel"> {
  patientId: string;
  v: number;
  withdrawnReason: string | null;
  versions: Omit<FindingVersion, "toothKey" | "surface" | "condition">[];
  links: FindingLink[];
}

export class FakeOdontogramStore {
  types = new Map<string, StoredType>();
  findings = new Map<string, StoredFinding>();
  private seq = 0;
  private clock = 0;
  private now() { return new Date(Date.UTC(2026, 9, 4, 14, 0, this.clock++)).toISOString(); }
  private refuse = (status: number, error: string, message: string, fieldErrors: Record<string, string> = {}) => json(status, { error, message, fieldErrors });
  private conflict = (entity: string, id: string) => json(409, { error: "concurrency_conflict", entityType: entity, entityId: id, message: "Changed by someone else." });

  constructor() {
    SEED.forEach((t, i) => this.types.set(t.code, { ...t, id: `ct-${i + 1}`, isActive: true, createdByName: "System", createdAtUtc: "2026-10-04T00:00:00Z", v: 1, events: [] }));
  }

  // ---------- seeding ----------
  addConditionType(over: Partial<ConditionType> & { code: string }): ConditionType {
    const t: StoredType = {
      id: over.id ?? `ct-${++this.seq + 100}`, code: over.code, label: over.label ?? over.code, scope: over.scope ?? "Surface", appliesTo: over.appliesTo ?? "Both", toothEffect: over.toothEffect ?? "None",
      isActive: over.isActive ?? true, createdByName: over.createdByName ?? ACTOR, createdAtUtc: over.createdAtUtc ?? "2026-10-01T10:00:00Z", v: 1,
      events: [{ eventNumber: 1, changeType: "Created", reason: null, actorName: over.createdByName ?? ACTOR, occurredAtUtc: over.createdAtUtc ?? "2026-10-01T10:00:00Z" }],
    };
    this.types.set(t.code, t);
    return this.typeView(t);
  }

  addFinding(patientId: string, over: Partial<Finding> & { toothKey: string }): Finding {
    const condition = over.condition ?? "Crown";
    const type = this.types.get(condition);
    const at = over.recordedAtUtc ?? "2026-09-01T10:00:00Z";
    const f: StoredFinding = {
      id: over.id ?? `fd-${++this.seq}`, patientId, toothKey: over.toothKey, surface: over.surface ?? null, condition, conditionScope: over.conditionScope ?? type?.scope ?? "WholeTooth", state: over.state ?? "Existing",
      status: over.status ?? "Active", recordedByName: over.recordedByName ?? ACTOR, recordedAtUtc: at, updatedByName: over.updatedByName ?? null, updatedAtUtc: over.updatedAtUtc ?? null,
      v: 1, withdrawnReason: null, links: [], versions: [{ versionNumber: 1, changeType: "Recorded", state: over.state ?? "Existing", status: "Active", reason: null, actorName: ACTOR, occurredAtUtc: at }],
    };
    this.findings.set(f.id, f);
    return this.view(f);
  }

  /** A link already made from a finding (as the server would hold it), recorded in the finding's history too. */
  addLink(findingId: string, linkType: string, reference: string) {
    const f = this.findings.get(findingId)!;
    const at = this.now();
    f.links.push({ linkType, reference, linkedByName: ACTOR, linkedAtUtc: at });
    f.versions.push({ versionNumber: f.versions.length + 1, changeType: "Linked", state: f.state, status: f.status, reason: `${linkType}: ${reference}`, actorName: ACTOR, occurredAtUtc: at });
  }

  /** Someone else changes the finding behind the screen's back: the version the screen holds is now stale. */
  bump(id: string) { this.findings.get(id)!.v++; }
  bumpType(code: string) { this.types.get(code)!.v++; }

  // ---------- views ----------
  private label(code: string) { return this.types.get(code)?.label ?? code; }
  private typeView(t: StoredType): ConditionType { const { v, events: _e, ...rest } = t; return { ...rest, rowVersion: `ctv${v}` }; }
  private view(f: StoredFinding): Finding {
    const { patientId: _p, v, withdrawnReason: _w, versions: _v, links, ...rest } = f;
    return { ...rest, conditionLabel: this.label(f.condition), rowVersion: `rv${v}`, links: links.map((l) => ({ ...l })) };
  }
  typeList(): ConditionType[] { return [...this.types.values()].sort((a, b) => Number(b.isActive) - Number(a.isActive) || a.label.localeCompare(b.label)).map((t) => this.typeView(t)); }
  chart(patientId: string): Chart {
    const findings = [...this.findings.values()].filter((f) => f.patientId === patientId && f.status === "Active").map((f) => this.view(f))
      .sort((a, b) => a.toothKey.localeCompare(b.toothKey) || (a.surface ?? "").localeCompare(b.surface ?? ""));
    return { patientId, findings };
  }

  // ---------- rules ----------
  private validate(b: Record<string, unknown>): Response | null {
    const errors: Record<string, string> = {};
    const s = (k: string) => (typeof b[k] === "string" ? (b[k] as string).trim() : "");
    const tooth = s("toothKey");
    const surface = s("surface").toUpperCase();
    const code = s("condition");
    const type = this.types.get(code);
    const toothOk = ALL_KEYS.includes(tooth);
    if (!toothOk) errors.toothKey = "Choose one of the teeth on the chart.";
    if (!code) errors.condition = "Choose the condition.";
    else if (!type) errors.condition = "That condition does not exist. Choose one from the list.";
    if (!(FINDING_STATES as readonly string[]).includes(s("state"))) errors.state = "Choose a state.";
    if (type) {
      if (toothOk && !(type.appliesTo === "Both" || type.appliesTo === (isPrimary(tooth) ? "Primary" : "Permanent"))) errors.condition = `${type.label} cannot be recorded on ${isPrimary(tooth) ? "primary" : "permanent"} teeth.`;
      if (type.scope === "Surface") {
        if (!surface) errors.surface = "Choose the surface this applies to.";
        else if (toothOk && !surfacesFor(tooth).includes(surface)) errors.surface = "That surface does not exist on this tooth.";
      } else if (surface) errors.surface = `${type.label} applies to the whole tooth, so no surface can be chosen.`;
    }
    if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
    if (!type!.isActive) return this.refuse(409, "condition_inactive", `${type!.label} is no longer in use, so it cannot be recorded on a new finding. Choose another condition.`, { condition: "This condition has been retired." });
    return null;
  }

  private stale(entity: string, id: string, current: number, prefix: string, body: Record<string, unknown> | null): Response | null {
    const rv = typeof body?.rowVersion === "string" ? body.rowVersion : "";
    if (!rv) return this.refuse(400, "row_version_required", "The version you are working on is required.");
    return rv === `${prefix}${current}` ? null : this.conflict(entity, id);
  }

  route(path: string, method: string, body: Record<string, unknown> | null): Response | null {
    const parts = path.split("/");
    // /api/patients/{id}/odontogram[/findings | /teeth/{key}/history]
    if (parts[2] === "patients" && parts[4] === "odontogram") {
      const patientId = parts[3];
      if (method === "GET" && parts.length === 5) return json(200, this.chart(patientId));
      if (method === "GET" && parts[5] === "teeth" && parts[7] === "history") {
        if (!ALL_KEYS.includes(parts[6])) return this.refuse(400, "validation_failed", "Some fields need attention.", { toothKey: "Choose one of the teeth on the chart." });
        const events: ToothEvent[] = [...this.findings.values()].filter((f) => f.patientId === patientId && f.toothKey === parts[6])
          .flatMap((f) => f.versions.map((v) => ({ findingId: f.id, condition: f.condition, conditionLabel: this.label(f.condition), surface: f.surface, versionNumber: v.versionNumber, changeType: v.changeType, state: v.state, status: v.status, reason: v.reason, actorName: v.actorName, occurredAtUtc: v.occurredAtUtc })))
          .sort((a, b) => a.occurredAtUtc.localeCompare(b.occurredAtUtc) || a.findingId.localeCompare(b.findingId) || a.versionNumber - b.versionNumber);
        return json(200, { patientId, toothKey: parts[6], events });
      }
      if (method === "POST" && parts[5] === "findings" && body) {
        const refused = this.validate(body);
        if (refused) return refused;
        const type = this.types.get(String(body.condition).trim())!;
        const tooth = String(body.toothKey).trim();
        const surface = typeof body.surface === "string" && body.surface.trim() ? body.surface.trim().toUpperCase() : null;
        const twin = [...this.findings.values()].find((f) => f.patientId === patientId && f.status === "Active" && f.toothKey === tooth && f.surface === surface && f.condition === type.code);
        if (twin) return twin.state === body.state ? json(200, this.chart(patientId)) : this.refuse(409, "finding_exists", `This is already recorded as ${twin.state}. Change its state instead of recording it again.`);
        if (type.toothEffect === "None") {
          const absent = [...this.findings.values()].some((f) => f.patientId === patientId && f.toothKey === tooth && f.status === "Active" && this.types.get(f.condition)?.toothEffect === "Absent" && (f.state === "Existing" || f.state === "Completed"));
          if (absent) return this.refuse(409, "tooth_absent", "This tooth is recorded as missing, so no other finding can be recorded on it. Record an implant, or withdraw the missing-tooth finding if it was a mistake.");
        }
        const at = this.now();
        const f = this.addFinding(patientId, { toothKey: tooth, surface, condition: type.code, conditionScope: type.scope, state: body.state as Finding["state"], recordedAtUtc: at });
        this.findings.get(f.id)!.versions[0].occurredAtUtc = at;
        return json(200, this.chart(patientId));
      }
    }
    // /api/odontogram/findings/{id}[/history|/state|/withdraw|/links]
    if (parts[2] === "odontogram" && parts[3] === "findings") {
      const f = this.findings.get(parts[4]);
      if (!f) return this.refuse(404, "finding_not_found", "That finding was not found.");
      if (method === "GET" && parts[5] === "history")
        return json(200, { findingId: f.id, versions: f.versions.map((v) => ({ ...v, toothKey: f.toothKey, surface: f.surface, condition: f.condition })) });
      if (method === "POST" && parts[5] === "state") {
        const to = typeof body?.state === "string" ? body.state : "";
        if (!(FINDING_STATES as readonly string[]).includes(to)) return this.refuse(400, "validation_failed", "Some fields need attention.", { state: "Choose a state." });
        const stale = this.stale("ToothFinding", f.id, f.v, "rv", body);
        if (stale) return stale;
        if (f.status === "Withdrawn") return this.refuse(409, "finding_withdrawn", "This finding was withdrawn, so its state cannot change.");
        if (f.state === to) return json(200, this.chart(f.patientId));
        if (!(FORWARD[f.state] ?? []).includes(to)) return this.refuse(409, "invalid_transition", `A finding that is ${f.state} cannot become ${to}. If it was entered wrongly, withdraw it and record it again.`);
        f.state = to as Finding["state"]; f.v++; f.updatedByName = ACTOR; f.updatedAtUtc = this.now();
        f.versions.push({ versionNumber: f.versions.length + 1, changeType: "StateChanged", state: to, status: "Active", reason: null, actorName: ACTOR, occurredAtUtc: f.updatedAtUtc });
        return json(200, this.chart(f.patientId));
      }
      if (method === "POST" && parts[5] === "withdraw") {
        const reason = typeof body?.reason === "string" ? body.reason.trim() : "";
        if (!reason) return this.refuse(400, "reason_required", "Say why this finding is being withdrawn.", { reason: "Say why this finding is being withdrawn." });
        const stale = this.stale("ToothFinding", f.id, f.v, "rv", body);
        if (stale) return stale;
        if (f.status === "Withdrawn") return json(200, this.chart(f.patientId));
        f.status = "Withdrawn"; f.withdrawnReason = reason; f.v++; f.updatedByName = ACTOR; f.updatedAtUtc = this.now();
        f.versions.push({ versionNumber: f.versions.length + 1, changeType: "Withdrawn", state: f.state, status: "Withdrawn", reason, actorName: ACTOR, occurredAtUtc: f.updatedAtUtc });
        return json(200, this.chart(f.patientId));
      }
      if (method === "POST" && parts[5] === "links") {
        const type = typeof body?.linkType === "string" ? body.linkType.trim() : "";
        const reference = typeof body?.reference === "string" ? body.reference.trim() : "";
        const errors: Record<string, string> = {};
        if (!(LINK_TYPES as readonly string[]).includes(type)) errors.linkType = "Choose Diagnosis, TreatmentPlan, Procedure.";
        if (!reference) errors.reference = "Say which record this links to.";
        if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
        if (f.status === "Withdrawn") return this.refuse(409, "finding_withdrawn", "This finding was withdrawn, so it cannot be linked.");
        if (!f.links.some((l) => l.linkType === type && l.reference === reference)) {
          const at = this.now();
          f.links.push({ linkType: type, reference, linkedByName: ACTOR, linkedAtUtc: at });
          f.versions.push({ versionNumber: f.versions.length + 1, changeType: "Linked", state: f.state, status: f.status, reason: `${type}: ${reference}`, actorName: ACTOR, occurredAtUtc: at });
        }
        return json(200, this.chart(f.patientId));
      }
    }
    // /api/odontogram/condition-types[/{id}/history|retire|reactivate]
    if (parts[2] === "odontogram" && parts[3] === "condition-types") {
      if (method === "GET" && parts.length === 4) return json(200, this.typeList());
      if (method === "POST" && parts.length === 4 && body) return this.createType(body);
      const t = [...this.types.values()].find((x) => x.id === parts[4]);
      if (!t) return this.refuse(404, "condition_not_found", "That condition type was not found.");
      if (method === "GET" && parts[5] === "history") return json(200, t.events);
      if (method === "POST" && (parts[5] === "retire" || parts[5] === "reactivate")) {
        const reason = typeof body?.reason === "string" ? body.reason.trim() : "";
        if (parts[5] === "retire" && !reason) return this.refuse(400, "reason_required", "Say why this condition is being retired.", { reason: "Say why this condition is being retired." });
        const stale = this.stale("ConditionType", t.id, t.v, "ctv", body);
        if (stale) return stale;
        const wantActive = parts[5] === "reactivate";
        if (t.isActive !== wantActive) {
          t.isActive = wantActive; t.v++;
          t.events.push({ eventNumber: t.events.length + 1, changeType: wantActive ? "Reactivated" : "Retired", reason: reason || null, actorName: ACTOR, occurredAtUtc: this.now() });
        }
        return json(200, this.typeList());
      }
    }
    return null;
  }

  private createType(b: Record<string, unknown>): Response {
    const s = (k: string) => (typeof b[k] === "string" ? (b[k] as string).trim() : "");
    const errors: Record<string, string> = {};
    if (!CODE_SHAPE.test(s("code"))) errors.code = "Use 2 to 32 letters and digits, starting with a capital letter (for example Fracture).";
    if (!s("label")) errors.label = "A label is required.";
    if (!(CONDITION_SCOPES as readonly string[]).includes(s("scope"))) errors.scope = "Choose Surface or WholeTooth.";
    if (!(CONDITION_DENTITIONS as readonly string[]).includes(s("appliesTo"))) errors.appliesTo = "Choose Permanent, Primary or Both.";
    const effect = s("toothEffect") || "None";
    if (!(TOOTH_EFFECTS as readonly string[]).includes(effect)) errors.toothEffect = "Choose None, Absent or Replacement.";
    if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
    const twin = [...this.types.values()].find((t) => t.code.toLowerCase() === s("code").toLowerCase());
    if (twin) {
      const same = twin.code === s("code") && twin.label === s("label") && twin.scope === s("scope") && twin.appliesTo === s("appliesTo") && twin.toothEffect === effect;
      return same ? json(200, this.typeList()) : this.refuse(409, "condition_exists", `There is already a condition with the code '${s("code")}'. Choose another code.`, { code: "That code is already used." });
    }
    this.addConditionType({ code: s("code"), label: s("label"), scope: s("scope") as ConditionType["scope"], appliesTo: s("appliesTo") as ConditionType["appliesTo"], toothEffect: effect as ConditionType["toothEffect"], createdAtUtc: this.now() });
    return json(200, this.typeList());
  }
}
