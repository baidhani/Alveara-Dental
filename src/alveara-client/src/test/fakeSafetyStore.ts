import { json } from "./fakePatientServer";
import type { Clearance, SafetyContext, SafetyEntry, SafetyGap, SafetySeverity, SafetySummary } from "../services/safetyApi";

/**
 * ALV-N011: an in-memory stand-in for the patient-safety API (Controllers/SafetyController). It models the response SHAPES, the row-version and revision checks (a stale write is the
 * shared 409 concurrency conflict; acknowledging an old revision is `alert_changed`), and the rules a UI has to render: an alert needs a source, resolving and cancelling need a
 * reason, a clearance still waiting cannot be resolved, acknowledging never resolves. The real rules are proven by the backend tests and the real-backend walkthrough; this exists so
 * the UI's handling of each outcome can be tested in isolation. A patient with nothing seeded has an EMPTY context (and the three "not established" gaps) - nothing is invented.
 */
interface StoredAlert {
  id: string; patientId: string; category: string; title: string; detail: string | null; severity: SafetySeverity; source: string; status: "Active" | "Resolved";
  revision: number; v: number; at: string; by: string; resolvedAt: string | null; resolvedBy: string | null; reason: string | null; acked: boolean; attention: string | null;
  versions: { n: number; type: string; reason: string | null }[];
}
interface StoredClearance extends Omit<Clearance, "rowVersion" | "documentPending"> { patientId: string; v: number }

const ACTOR = "Dr. Okafor";
const SEVERITIES: SafetySeverity[] = ["Critical", "High", "Moderate", "Low"];
const CATEGORIES = ["Condition", "Pregnancy", "Anticoagulant", "AdverseReaction", "Custom"];

export class FakeSafetyStore {
  alerts = new Map<string, StoredAlert>();
  clearances = new Map<string, StoredClearance>();
  /** Record-origin entries (allergies, medications) per patient: read-only here, as in the real system. */
  recordEntries = new Map<string, SafetyEntry[]>();
  /** Per patient: the sections that ARE established (so they produce no gap). Default: none established. */
  established = new Map<string, Set<string>>();
  private seq = 0;
  private clock = 0;
  private now() { return new Date(Date.UTC(2026, 9, 3, 14, 0, this.clock++)).toISOString(); }

  // ---------- seeding ----------
  addAlert(patientId: string, over: Partial<StoredAlert> = {}): StoredAlert {
    const a: StoredAlert = {
      id: over.id ?? `al-${++this.seq}`, patientId, category: over.category ?? "Condition", title: over.title ?? "Prosthetic heart valve", detail: over.detail ?? null, severity: over.severity ?? "High",
      source: over.source ?? "Reported by the patient at intake", status: over.status ?? "Active", revision: over.revision ?? 1, v: 1, at: over.at ?? "2026-09-01T10:00:00Z", by: over.by ?? ACTOR,
      resolvedAt: over.resolvedAt ?? null, resolvedBy: over.resolvedBy ?? null, reason: over.reason ?? null, acked: over.acked ?? false, attention: over.attention ?? null,
      versions: [{ n: 1, type: "Created", reason: null }],
    };
    this.alerts.set(a.id, a);
    return a;
  }

  addClearance(patientId: string, over: Partial<StoredClearance> = {}): StoredClearance {
    const c: StoredClearance = {
      id: over.id ?? `cl-${++this.seq}`, kind: over.kind ?? "Medical", reason: over.reason ?? "Cardiac clearance before extraction", requestedFrom: over.requestedFrom ?? "Dr. Singh", status: over.status ?? "Requested",
      documentReference: over.documentReference ?? null, requestedAtUtc: over.requestedAtUtc ?? "2026-09-02T10:00:00Z", requestedByName: ACTOR, receivedAtUtc: over.receivedAtUtc ?? null, receivedByName: over.receivedByName ?? null,
      receivedNote: over.receivedNote ?? null, closedAtUtc: over.closedAtUtc ?? null, closedByName: over.closedByName ?? null, closingReason: over.closingReason ?? null, updatedAtUtc: null, patientId, v: 1,
    };
    this.clearances.set(c.id, c);
    return c;
  }

  addRecordEntry(patientId: string, over: Partial<SafetyEntry> & { title: string; category: "Allergy" | "Medication" }): SafetyEntry {
    const e: SafetyEntry = {
      origin: "ClinicalRecord", id: `rec-${++this.seq}`, detail: null, severity: null, status: "Active", source: over.category === "Allergy" ? "Clinical record: allergies" : "Clinical record: medications", sourceItemId: null,
      lastUpdatedAtUtc: "2026-09-01T10:00:00Z", lastUpdatedByName: ACTOR, needsAttention: false, attentionReason: null, acknowledgedByMe: false, acknowledgedAtUtc: null, revision: null, rowVersion: null,
      resolvedAtUtc: null, resolvedByName: null, resolutionReason: null, ...over,
    };
    this.recordEntries.set(patientId, [...(this.recordEntries.get(patientId) ?? []), e]);
    return e;
  }

  establish(patientId: string, ...sections: string[]) { this.established.set(patientId, new Set([...(this.established.get(patientId) ?? []), ...sections])); }

  // ---------- views ----------
  private entryOf(a: StoredAlert): SafetyEntry {
    return {
      origin: "Alert", id: a.id, category: a.category, title: a.title, detail: a.detail, severity: a.severity, status: a.status, source: a.source, sourceItemId: null, lastUpdatedAtUtc: a.at, lastUpdatedByName: a.by,
      needsAttention: a.attention !== null, attentionReason: a.attention, acknowledgedByMe: a.acked, acknowledgedAtUtc: a.acked ? "2026-10-03T14:30:00Z" : null, revision: a.revision, rowVersion: `a${a.v}`,
      resolvedAtUtc: a.resolvedAt, resolvedByName: a.resolvedBy, resolutionReason: a.reason,
    };
  }

  private clearanceView(c: StoredClearance): Clearance {
    const { v, patientId: _patientId, ...rest } = c;
    return { ...rest, documentPending: (c.status === "Received" || c.status === "Resolved") && c.documentReference === null, rowVersion: `c${v}` };
  }

  context(patientId: string): SafetyContext {
    const mine = [...this.alerts.values()].filter((a) => a.patientId === patientId);
    const active = [...(this.recordEntries.get(patientId) ?? []), ...mine.filter((a) => a.status === "Active").map((a) => this.entryOf(a))]
      .sort((x, y) => SEVERITIES.indexOf(x.severity ?? ("Low" as SafetySeverity)) - SEVERITIES.indexOf(y.severity ?? ("Low" as SafetySeverity)) || (x.severity === null ? 1 : 0) - (y.severity === null ? 1 : 0));
    const resolved = mine.filter((a) => a.status === "Resolved").map((a) => this.entryOf(a));
    const clearances = [...this.clearances.values()].filter((c) => c.patientId === patientId)
      .sort((a, b) => Number(b.status === "Requested" || b.status === "Received") - Number(a.status === "Requested" || a.status === "Received")).map((c) => this.clearanceView(c));
    const est = this.established.get(patientId) ?? new Set<string>();
    const gaps: SafetyGap[] = [["Allergy", "Allergies"], ["Medication", "Medications"], ["MedicalHistory", "Medical history"]]
      .filter(([k]) => !est.has(k)).map(([k, l]) => ({ section: k, status: "NotReviewed", message: `${l} have not been reviewed. Nothing listed here does not mean none.` }));
    const summary: SafetySummary = {
      patientId, asOfUtc: "2026-10-03T14:00:00Z", activeAlertCount: active.filter((e) => e.origin === "Alert").length, activeAllergyCount: active.filter((e) => e.category === "Allergy").length,
      currentMedicationCount: active.filter((e) => e.category === "Medication").length, highestSeverity: SEVERITIES.find((s) => active.some((e) => e.severity === s)) ?? null,
      openClearanceCount: clearances.filter((c) => c.status === "Requested" || c.status === "Received").length, unacknowledgedAlertCount: active.filter((e) => e.origin === "Alert" && !e.acknowledgedByMe).length,
      needsAttentionCount: active.filter((e) => e.needsAttention).length, gaps,
    };
    return { patientId, asOfUtc: summary.asOfUtc, summary, entries: active, resolved, clearances, gaps };
  }

  // ---------- routing ----------
  private refuse = (status: number, error: string, message: string, fieldErrors: Record<string, string> = {}) => json(status, { error, message, fieldErrors });
  private conflict = (entityType: string, id: string) => json(409, { error: "concurrency_conflict", entityType, entityId: id, message: "Changed by someone else." });
  private text = (b: Record<string, unknown> | null, k: string) => (typeof b?.[k] === "string" ? (b[k] as string).trim() : "");

  /** Answers a safety request, or null when the path is not one of them. */
  route(path: string, method: string, body: Record<string, unknown> | null): Response | null {
    const parts = path.split("/");
    if (parts[2] === "patients" && parts[4] === "safety") {
      const patientId = parts[3];
      if (method === "GET" && parts.length === 5) return json(200, this.context(patientId));
      if (method === "GET" && parts[5] === "summary") return json(200, this.context(patientId).summary);
      if (method === "POST" && parts[5] === "alerts") {
        const errors: Record<string, string> = {};
        if (!CATEGORIES.includes(this.text(body, "category"))) errors.category = "Choose a kind of alert.";
        if (!this.text(body, "title")) errors.title = "A title is required.";
        if (!SEVERITIES.includes(this.text(body, "severity") as SafetySeverity)) errors.severity = "Choose Critical, High, Moderate or Low.";
        if (!this.text(body, "sourceNote")) errors.sourceNote = "A source is required.";
        if (Object.keys(errors).length === 1 && errors.sourceNote) return this.refuse(400, "source_required", "Say where this information came from.", errors);
        if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
        const twin = [...this.alerts.values()].find((a) => a.patientId === patientId && a.status === "Active" && a.category === this.text(body, "category") && a.title.toLowerCase() === this.text(body, "title").toLowerCase());
        if (twin) {
          const same = twin.detail === (this.text(body, "detail") || null) && twin.severity === this.text(body, "severity") && twin.source === this.text(body, "sourceNote");
          return same ? json(200, this.context(patientId)) : this.refuse(409, "duplicate_alert", `There is already an active alert named '${twin.title}'. Change that alert instead.`); // a retried create is a quiet repeat
        }
        this.addAlert(patientId, { category: this.text(body, "category"), title: this.text(body, "title"), detail: this.text(body, "detail") || null, severity: this.text(body, "severity") as SafetySeverity, source: this.text(body, "sourceNote"), at: this.now() });
        return json(200, this.context(patientId));
      }
      if (method === "POST" && parts[5] === "clearances") {
        const errors: Record<string, string> = {};
        if (!["Medical", "Dental"].includes(this.text(body, "kind"))) errors.kind = "Choose Medical or Dental.";
        if (!this.text(body, "reason")) errors.reason = "Say why the clearance is needed.";
        if (Object.keys(errors).length > 0) return this.refuse(400, "validation_failed", "Some fields need attention.", errors);
        this.addClearance(patientId, { kind: this.text(body, "kind"), reason: this.text(body, "reason"), requestedFrom: this.text(body, "requestedFrom") || null, requestedAtUtc: this.now() });
        return json(200, this.context(patientId));
      }
    }
    if (parts[2] === "safety" && parts[3] === "alerts" && parts[4]) return this.alertRoute(parts, method, body);
    if (parts[2] === "safety" && parts[3] === "clearances" && parts[4]) return this.clearanceRoute(parts, method, body);
    return null;
  }

  private alertRoute(parts: string[], method: string, body: Record<string, unknown> | null): Response {
    const a = this.alerts.get(parts[4]);
    if (!a) return this.refuse(404, "alert_not_found", "That alert was not found.");
    if (method === "GET" && parts[5] === "history") return json(200, { alertId: a.id, versions: a.versions.map((v) => ({ versionNumber: v.n, changeType: v.type, category: a.category, title: a.title, detail: a.detail, severity: a.severity, sourceNote: a.source, status: a.status, reason: v.reason, actorName: ACTOR, occurredAtUtc: a.at })) });
    if (parts[5] === "acknowledge" && method === "POST") {
      if (body?.revision !== a.revision) return this.refuse(409, "alert_changed", "This alert changed since you opened it. Review the new version, then acknowledge it.");
      if (a.status === "Resolved") return this.refuse(409, "alert_resolved", "This alert is resolved, so there is nothing to acknowledge.");
      a.acked = true;
      return json(200, this.context(a.patientId));
    }
    if (typeof body?.rowVersion !== "string" || body.rowVersion === "") return this.refuse(400, "row_version_required", "The version you are working on is required.");
    if (body.rowVersion !== `a${a.v}`) return this.conflict("SafetyAlert", a.id);
    const touch = (type: string, reason: string | null) => { a.v++; a.revision++; a.acked = false; a.at = this.now(); a.by = ACTOR; a.versions.push({ n: a.versions.length + 1, type, reason }); };
    if (method === "PUT") {
      if (a.status === "Resolved") return this.refuse(409, "alert_resolved", "This alert is resolved. Reopen it (with a reason) before changing it.");
      if (!this.text(body, "title") || !this.text(body, "sourceNote")) return this.refuse(400, "validation_failed", "Some fields need attention.", { ...(this.text(body, "title") ? {} : { title: "A title is required." }), ...(this.text(body, "sourceNote") ? {} : { sourceNote: "A source is required." }) });
      Object.assign(a, { title: this.text(body, "title"), detail: this.text(body, "detail") || null, severity: this.text(body, "severity") as SafetySeverity, source: this.text(body, "sourceNote") });
      touch("Changed", this.text(body, "reason") || null);
      return json(200, this.context(a.patientId));
    }
    const reason = this.text(body, "reason");
    if (parts[5] === "resolve" && method === "POST") {
      if (!reason) return this.refuse(400, "reason_required", "Say why this alert is resolved.", { reason: "Say why this alert is resolved." });
      if (a.status === "Active") { a.status = "Resolved"; a.resolvedAt = this.now(); a.resolvedBy = ACTOR; a.reason = reason; touch("Resolved", reason); }
      return json(200, this.context(a.patientId));
    }
    if (parts[5] === "reopen" && method === "POST") {
      if (!reason) return this.refuse(400, "reason_required", "Say why this alert is being reopened.", { reason: "Say why this alert is being reopened." });
      if (a.status === "Resolved") { a.status = "Active"; a.resolvedAt = null; a.resolvedBy = null; a.reason = null; touch("Reopened", reason); }
      return json(200, this.context(a.patientId));
    }
    return this.refuse(404, "not_found", "Not found.");
  }

  private clearanceRoute(parts: string[], method: string, body: Record<string, unknown> | null): Response {
    const c = this.clearances.get(parts[4]);
    if (!c) return this.refuse(404, "clearance_not_found", "That clearance was not found.");
    const patientId = c.patientId;
    if (method === "GET" && parts[5] === "history") return json(200, { clearanceId: c.id, versions: [{ versionNumber: 1, changeType: "Requested", kind: c.kind, reason: c.reason, requestedFrom: c.requestedFrom, status: "Requested", documentReference: null, note: null, actorName: ACTOR, occurredAtUtc: c.requestedAtUtc }] });
    if (typeof body?.rowVersion !== "string" || body.rowVersion === "") return this.refuse(400, "row_version_required", "The version you are working on is required.");
    if (body.rowVersion !== `c${c.v}`) return this.conflict("Clearance", c.id);
    const reason = this.text(body, "reason");
    const closed = c.status === "Resolved" || c.status === "Cancelled";
    if (parts[5] === "receive") {
      if (closed) return this.refuse(409, "clearance_closed", `This clearance is already ${c.status.toLowerCase()} and cannot be changed.`);
      if (c.status === "Requested") { Object.assign(c, { status: "Received", receivedAtUtc: this.now(), receivedByName: ACTOR, receivedNote: this.text(body, "note") || null, documentReference: this.text(body, "documentReference") || null }); c.v++; }
      return json(200, this.context(patientId));
    }
    if (parts[5] === "document") {
      if (!this.text(body, "documentReference")) return this.refuse(400, "validation_failed", "Some fields need attention.", { documentReference: "Say which document this is." });
      if (c.status === "Requested") return this.refuse(409, "clearance_not_received", "The clearance has not been received yet, so there is no document to attach.");
      if (c.status === "Cancelled") return this.refuse(409, "clearance_closed", "This clearance is already cancelled and cannot be changed.");
      c.documentReference = this.text(body, "documentReference"); c.v++;
      return json(200, this.context(patientId));
    }
    if (parts[5] === "resolve") {
      if (!reason) return this.refuse(400, "reason_required", "Say why this clearance is resolved.", { reason: "Say why this clearance is resolved." });
      if (c.status === "Requested") return this.refuse(409, "clearance_not_received", "A clearance must be received before it is resolved. Record that it arrived first.");
      if (c.status === "Cancelled") return this.refuse(409, "clearance_closed", "This clearance is already cancelled and cannot be changed.");
      if (c.status === "Received") { Object.assign(c, { status: "Resolved", closedAtUtc: this.now(), closedByName: ACTOR, closingReason: reason }); c.v++; }
      return json(200, this.context(patientId));
    }
    if (parts[5] === "cancel") {
      if (!reason) return this.refuse(400, "reason_required", "Say why this clearance is no longer needed.", { reason: "Say why this clearance is no longer needed." });
      if (c.status === "Resolved") return this.refuse(409, "clearance_closed", "This clearance is already resolved and cannot be changed.");
      if (c.status !== "Cancelled") { Object.assign(c, { status: "Cancelled", closedAtUtc: this.now(), closedByName: ACTOR, closingReason: reason }); c.v++; }
      return json(200, this.context(patientId));
    }
    return this.refuse(404, "not_found", "Not found.");
  }
}
