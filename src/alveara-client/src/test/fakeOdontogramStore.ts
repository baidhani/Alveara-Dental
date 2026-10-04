import { ALL_KEYS, surfacesFor } from "../pages/odontogram/toothNumbering";
import { CONDITIONS, FINDING_STATES, SURFACE_CONDITIONS } from "../services/odontogramApi";
import type { Chart, Finding, FindingVersion } from "../services/odontogramApi";
import { json } from "./fakePatientServer";

/**
 * STORY-006: an in-memory stand-in for the odontogram API (Controllers/OdontogramController). It models the response SHAPES, the row-version check (a stale write is the shared 409
 * concurrency conflict), and the rules a UI has to render: a tooth must be one of the 52, a surface must exist on that kind of tooth, a surface condition needs a surface and a
 * whole-tooth condition must not have one, a state only moves forward (Diagnosed, Planned, Completed), a withdrawal needs a reason, repeats are quiet. The real rules are proven by
 * the backend tests and the real-backend walkthrough; this exists so the UI's handling of each outcome can be tested in isolation. A patient with nothing seeded has an EMPTY chart.
 */
const ACTOR = "Dr. Okafor";
const FORWARD: Record<string, string[]> = { Diagnosed: ["Planned", "Completed"], Planned: ["Completed"] };

interface Stored extends Omit<Finding, "rowVersion"> {
  patientId: string;
  v: number;
  withdrawnReason: string | null;
  versions: Omit<FindingVersion, "toothKey" | "surface" | "condition">[];
}

export class FakeOdontogramStore {
  findings = new Map<string, Stored>();
  private seq = 0;
  private clock = 0;
  private now() { return new Date(Date.UTC(2026, 9, 4, 14, 0, this.clock++)).toISOString(); }
  private refuse = (status: number, error: string, message: string, fieldErrors: Record<string, string> = {}) => json(status, { error, message, fieldErrors });
  private conflict = (id: string) => json(409, { error: "concurrency_conflict", entityType: "ToothFinding", entityId: id, message: "Changed by someone else." });

  addFinding(patientId: string, over: Partial<Finding> & { toothKey: string }): Finding {
    const f: Stored = {
      id: over.id ?? `fd-${++this.seq}`, patientId, toothKey: over.toothKey, surface: over.surface ?? null, condition: over.condition ?? "Crown", state: over.state ?? "Existing", status: over.status ?? "Active",
      recordedByName: over.recordedByName ?? ACTOR, recordedAtUtc: over.recordedAtUtc ?? "2026-09-01T10:00:00Z", updatedByName: over.updatedByName ?? null, updatedAtUtc: over.updatedAtUtc ?? null,
      v: 1, withdrawnReason: null, versions: [{ versionNumber: 1, changeType: "Recorded", state: over.state ?? "Existing", status: "Active", reason: null, actorName: ACTOR, occurredAtUtc: over.recordedAtUtc ?? "2026-09-01T10:00:00Z" }],
    };
    this.findings.set(f.id, f);
    return this.view(f);
  }

  /** Someone else changes the finding behind the screen's back: the version the screen holds is now stale. */
  bump(id: string) { this.findings.get(id)!.v++; }

  private view(f: Stored): Finding {
    const { patientId: _p, v, withdrawnReason: _w, versions: _v, ...rest } = f;
    return { ...rest, rowVersion: `rv${v}` };
  }

  chart(patientId: string): Chart {
    const findings = [...this.findings.values()].filter((f) => f.patientId === patientId && f.status === "Active").map((f) => this.view(f))
      .sort((a, b) => a.toothKey.localeCompare(b.toothKey) || (a.surface ?? "").localeCompare(b.surface ?? ""));
    return { patientId, findings };
  }

  private validate(b: Record<string, unknown>): Record<string, string> {
    const errors: Record<string, string> = {};
    const s = (k: string) => (typeof b[k] === "string" ? (b[k] as string).trim() : "");
    const tooth = s("toothKey");
    const surface = s("surface").toUpperCase();
    const condition = s("condition");
    if (!ALL_KEYS.includes(tooth)) errors.toothKey = "Choose one of the teeth on the chart.";
    if (!(CONDITIONS as readonly string[]).includes(condition)) errors.condition = "Choose a condition.";
    if (!(FINDING_STATES as readonly string[]).includes(s("state"))) errors.state = "Choose a state.";
    if ((CONDITIONS as readonly string[]).includes(condition)) {
      if (SURFACE_CONDITIONS.has(condition)) {
        if (!surface) errors.surface = "Choose the surface this applies to.";
        else if (ALL_KEYS.includes(tooth) && !surfacesFor(tooth).includes(surface)) errors.surface = "That surface does not exist on this tooth.";
      } else if (surface) errors.surface = `${condition} applies to the whole tooth, so no surface can be chosen.`;
    }
    return errors;
  }

  private stale(f: Stored, body: Record<string, unknown> | null): Response | null {
    const rv = typeof body?.rowVersion === "string" ? body.rowVersion : "";
    if (!rv) return this.refuse(400, "row_version_required", "The version you are working on is required.");
    return rv === `rv${f.v}` ? null : this.conflict(f.id);
  }

  route(path: string, method: string, body: Record<string, unknown> | null): Response | null {
    const parts = path.split("/");
    // /api/patients/{id}/odontogram[/findings]
    if (parts[2] === "patients" && parts[4] === "odontogram") {
      const patientId = parts[3];
      if (method === "GET" && parts.length === 5) return json(200, this.chart(patientId));
      if (method === "POST" && parts[5] === "findings" && body) {
        const errors = this.validate(body);
        if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
        const surface = typeof body.surface === "string" && body.surface.trim() ? body.surface.trim().toUpperCase() : null;
        const twin = [...this.findings.values()].find((f) => f.patientId === patientId && f.status === "Active" && f.toothKey === body.toothKey && f.surface === surface && f.condition === body.condition);
        if (twin) return twin.state === body.state ? json(200, this.chart(patientId)) : this.refuse(409, "finding_exists", `This is already recorded as ${twin.state}. Change its state instead of recording it again.`);
        const f = this.addFinding(patientId, { toothKey: body.toothKey as string, surface, condition: body.condition as Finding["condition"], state: body.state as Finding["state"], recordedAtUtc: this.now() });
        this.findings.get(f.id)!.versions[0].occurredAtUtc = f.recordedAtUtc;
        return json(200, this.chart(patientId));
      }
    }
    // /api/odontogram/findings/{id}[/history|/state|/withdraw]
    if (parts[2] === "odontogram" && parts[3] === "findings") {
      const f = this.findings.get(parts[4]);
      if (!f) return this.refuse(404, "finding_not_found", "That finding was not found.");
      if (method === "GET" && parts[5] === "history")
        return json(200, { findingId: f.id, versions: f.versions.map((v) => ({ ...v, toothKey: f.toothKey, surface: f.surface, condition: f.condition })) });
      if (method === "POST" && parts[5] === "state") {
        const to = typeof body?.state === "string" ? body.state : "";
        if (!(FINDING_STATES as readonly string[]).includes(to)) return this.refuse(400, "validation_failed", "Some fields need attention.", { state: "Choose a state." });
        const stale = this.stale(f, body);
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
        const stale = this.stale(f, body);
        if (stale) return stale;
        if (f.status === "Withdrawn") return json(200, this.chart(f.patientId));
        f.status = "Withdrawn"; f.withdrawnReason = reason; f.v++; f.updatedByName = ACTOR; f.updatedAtUtc = this.now();
        f.versions.push({ versionNumber: f.versions.length + 1, changeType: "Withdrawn", state: f.state, status: "Withdrawn", reason, actorName: ACTOR, occurredAtUtc: f.updatedAtUtc });
        return json(200, this.chart(f.patientId));
      }
    }
    return null;
  }
}
