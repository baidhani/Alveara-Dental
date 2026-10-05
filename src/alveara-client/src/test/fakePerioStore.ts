import { json } from "./fakePatientServer";
import type { PerioChart, PerioComparison, PerioFigures, PerioProblem, PerioReading, PerioSession, PerioSiteChange, PerioToothChange } from "../services/perioApi";
import { ARCH_ORDER, DEEP_MM, SIGNIFICANT_CHANGE_MM, VERY_DEEP_MM, cuesOf, isMultiRooted, sweep } from "../pages/perio/perioSiteModel";

/**
 * STORY-012 and ALV-012-C01: an in-memory stand-in for the periodontal charting API (Controllers/PerioController and PerioSessionController): the response SHAPES, the server's validation (every problem
 * listed together, nothing partial saved), the idempotent save (the same key and readings return the chart already saved; the same key with different readings is a 409), chart sessions with a row
 * version (a stale edit is the shared 409 conflict, a refused batch changes nothing), finalizing, links and comparison. The real rules are proven by the backend tests and the real-backend
 * walkthrough; this exists so the UI's handling of each outcome can be tested in isolation.
 */
const PERMANENT = ["1", "2", "3", "4"].flatMap((q) => [1, 2, 3, 4, 5, 6, 7, 8].map((p) => `${q}${p}`));
const PRIMARY = ["5", "6", "7", "8"].flatMap((q) => [1, 2, 3, 4, 5].map((p) => `${q}${p}`));
const SITES = ["DB", "B", "MB", "DL", "L", "ML"];
const ACTOR = "Dr. Okafor";

interface StoredChart extends PerioChart { key: string }
interface StoredSession {
  id: string; patientId: string; status: "Draft" | "Finalized" | "Abandoned"; v: number; startedAtUtc: string; updatedAtUtc: string | null; examId: string | null;
  readings: Map<string, PerioReading>; teeth: Map<string, { toothKey: string; mobility: number | null; furcation: number | null; excluded: boolean }>;
}
type Body = Record<string, unknown> | null;

export class FakePerioStore {
  charts: StoredChart[] = [];
  sessions: StoredSession[] = [];
  /** Teeth the odontogram records as missing, for every patient. */
  absentTeeth: string[] = [];
  private seq = 0;
  private clock = 0;
  private now() { return new Date(Date.UTC(2026, 9, 5, 9, 0, this.clock++)).toISOString(); }

  /** A chart saved earlier (for history tests). */
  addChart(patientId: string, over: Partial<PerioChart> & { readings: PerioReading[] }): PerioChart {
    const chart: StoredChart = {
      id: over.id ?? `pe-${++this.seq}`, patientId, recordedAtUtc: over.recordedAtUtc ?? "2026-09-01T10:00:00Z", recordedByName: over.recordedByName ?? ACTOR,
      readingCount: over.readings.length, readings: over.readings, teeth: over.teeth ?? [], links: over.links ?? [], key: `seed-${this.seq}`,
    };
    this.charts.push(chart);
    return chart;
  }

  /** A draft someone started earlier (for resume tests). */
  addSession(patientId: string, over: { readings?: PerioReading[]; teeth?: PerioSession["teeth"] } = {}): StoredSession {
    const s: StoredSession = { id: `ps-${++this.seq}`, patientId, status: "Draft", v: 1, startedAtUtc: this.now(), updatedAtUtc: null, examId: null, readings: new Map(), teeth: new Map() };
    for (const r of over.readings ?? []) s.readings.set(`${r.toothKey}:${r.site}`, r);
    for (const t of over.teeth ?? []) s.teeth.set(t.toothKey, t);
    this.sessions.push(s);
    return s;
  }

  /** A stand-in for another person saving into the draft behind the screen's back: the version moves, so the next save from the screen is stale. */
  touchSession(id: string) { this.sessions.find((s) => s.id === id)!.v++; }

  private problems(readings: unknown): PerioProblem[] {
    if (!Array.isArray(readings) || readings.length === 0) return [{ toothKey: null, site: null, field: "readings", code: "required", message: "Enter at least one site reading before saving." }];
    const out: PerioProblem[] = [];
    const seen = new Set<string>();
    for (const r of readings as Record<string, unknown>[]) {
      const tooth = String(r.toothKey ?? ""), site = String(r.site ?? "");
      const at = { toothKey: tooth, site };
      if (!PERMANENT.includes(tooth) && !PRIMARY.includes(tooth)) out.push({ ...at, field: "toothKey", code: "unknown_tooth", message: `"${tooth}" is not a tooth. Use the two-digit FDI number, such as 16 or 47.` });
      else if (PRIMARY.includes(tooth)) out.push({ ...at, field: "toothKey", code: "primary_tooth", message: `Tooth ${tooth} is a primary tooth. Periodontal charting covers permanent teeth only.` });
      if (!SITES.includes(site)) out.push({ ...at, field: "site", code: "unknown_site", message: `"${site}" is not a site. Use one of ${SITES.join(", ")}.` });
      else if (seen.has(`${tooth}:${site}`)) out.push({ ...at, field: "site", code: "duplicate_site", message: `Tooth ${tooth} site ${site} is entered more than once; keep one reading.` });
      seen.add(`${tooth}:${site}`);
      for (const [field, label] of [["probingDepthMm", "Probing depth"], ["recessionMm", "Recession"]] as const) {
        const v = r[field];
        if (typeof v !== "number" || !Number.isInteger(v) || v < 0 || v > 15) out.push({ ...at, field, code: "out_of_range", message: `${label} at tooth ${tooth} site ${site} is ${String(v)} mm; it must be a whole number from 0 to 15 mm.` });
      }
    }
    return out;
  }

  /** Problems with a batch into a draft, judged against the draft as it would be after it (only the teeth the batch touches). */
  private batchProblems(s: StoredSession, b: Record<string, unknown>): PerioProblem[] {
    const readings = (b.readings as Record<string, unknown>[] | undefined) ?? [];
    const teeth = (b.teeth as Record<string, unknown>[] | undefined) ?? [];
    const problems = readings.length > 0 ? this.problems(readings) : [];
    for (const t of teeth) {
      const k = String(t.toothKey ?? "");
      for (const [field, label] of [["mobility", "Mobility"], ["furcation", "Furcation"]] as const) {
        const v = t[field];
        if (v !== null && v !== undefined && (typeof v !== "number" || !Number.isInteger(v) || v < 0 || v > 3)) problems.push({ toothKey: k, site: null, field, code: "out_of_range", message: `${label} at tooth ${k} is ${String(v)}; it must be a whole grade from 0 to 3.` });
      }
      if (t.furcation !== null && t.furcation !== undefined && !isMultiRooted(k)) problems.push({ toothKey: k, site: null, field: "furcation", code: "furcation_not_applicable", message: `Tooth ${k} has a single root, so there is no furcation to grade. Leave it empty.` });
    }
    if (problems.length > 0) return problems;
    const cleared = new Set(((b.clearSites as { toothKey: string; site: string }[] | undefined) ?? []).map((c) => `${c.toothKey}:${c.site}`));
    const excluded = new Set([...s.teeth.values()].filter((t) => t.excluded).map((t) => t.toothKey));
    for (const t of teeth) { if (t.excluded) excluded.add(String(t.toothKey)); else excluded.delete(String(t.toothKey)); }
    const incoming = new Set(readings.map((r) => `${r.toothKey}:${r.site}`));
    const merged = [...[...s.readings.keys()].filter((k) => !cleared.has(k) && !incoming.has(k)), ...incoming];
    const touched = new Set([...readings.map((r) => String(r.toothKey)), ...teeth.map((t) => String(t.toothKey))]);
    for (const key of merged) {
      const tooth = key.split(":")[0];
      if (touched.has(tooth) && excluded.has(tooth)) problems.push({ toothKey: tooth, site: key.split(":")[1], field: "excluded", code: "excluded_tooth", message: `Tooth ${tooth} is marked as not charted, so site ${key.split(":")[1]} cannot have a reading. Remove the reading or the exclusion.` });
    }
    for (const r of readings) {
      const tooth = String(r.toothKey);
      if (this.absentTeeth.includes(tooth) && !excluded.has(tooth)) problems.push({ toothKey: tooth, site: String(r.site), field: "toothKey", code: "tooth_absent", message: `Tooth ${tooth} is recorded as missing in the odontogram, so it cannot be charted. Mark it as not charted, or correct the odontogram first.` });
    }
    return problems;
  }

  private viewSession(s: StoredSession): PerioSession {
    const skipped = new Set([...[...s.teeth.values()].filter((t) => t.excluded).map((t) => t.toothKey), ...(s.status === "Draft" ? this.absentTeeth : [])]);
    const next = sweep(skipped).find((x) => !s.readings.has(`${x.toothKey}:${x.site}`)) ?? null;
    return {
      id: s.id, patientId: s.patientId, status: s.status, startedAtUtc: s.startedAtUtc, startedByName: ACTOR, updatedAtUtc: s.updatedAtUtc, updatedByName: s.updatedAtUtc ? ACTOR : null,
      closedAtUtc: s.status === "Draft" ? null : s.updatedAtUtc, examId: s.examId, rowVersion: `v${s.v}`,
      readings: [...s.readings.values()].sort((a, b) => ARCH_ORDER.indexOf(a.toothKey) - ARCH_ORDER.indexOf(b.toothKey) || SITES.indexOf(a.site) - SITES.indexOf(b.site)),
      teeth: [...s.teeth.values()].sort((a, b) => ARCH_ORDER.indexOf(a.toothKey) - ARCH_ORDER.indexOf(b.toothKey)),
      absentTeeth: s.status === "Draft" ? [...this.absentTeeth].sort() : [], next: next ? { toothKey: next.toothKey, site: next.site } : null,
      chartableSites: (32 - [...skipped].filter((k) => ARCH_ORDER.includes(k)).length) * 6,
    };
  }

  private conflict = (id: string) => json(409, { error: "concurrency_conflict", entityType: "PerioSession", entityId: id, message: "Changed by someone else." });
  private refuse = (status: number, error: string, message: string, problems: PerioProblem[] = []) => json(status, { error, message, problems });

  route(path: string, method: string, body: Body, url?: URL): Response | null {
    const parts = path.split("/");
    if (parts[2] === "patients" && parts[4] === "periodontal") return this.patientRoute(parts, method, body, url);
    if (parts[2] === "periodontal") return this.periodontalRoute(parts, method, body);
    return null;
  }

  private patientRoute(parts: string[], method: string, body: Body, url?: URL): Response | null {
    const patientId = parts[3];
    const what = parts[5];
    if (what === "session" && method === "GET") return json(200, { session: (() => { const s = this.sessions.find((x) => x.patientId === patientId && x.status === "Draft"); return s ? this.viewSession(s) : null; })() });
    if (what === "sessions" && method === "POST") {
      const open = this.sessions.find((x) => x.patientId === patientId && x.status === "Draft") ?? this.addSession(patientId);
      return json(200, this.viewSession(open));
    }
    if (what === "comparison" && method === "GET") return this.compare(patientId, url!);
    if (what !== "charts") return null;
    if (method === "GET") return json(200, { patientId, exams: this.charts.filter((c) => c.patientId === patientId).sort((a, b) => b.recordedAtUtc.localeCompare(a.recordedAtUtc)).map(({ key: _key, ...c }) => c) });
    if (method !== "POST") return null;
    return this.saveWholeChart(patientId, body);
  }

  private periodontalRoute(parts: string[], method: string, body: Body): Response | null {
    if (parts[3] === "charts") {
      const chart = this.charts.find((c) => c.id === parts[4]);
      if (!chart) return this.refuse(404, "exam_not_found", "That periodontal chart was not found.");
      if (method === "GET" && parts.length === 5) return json(200, this.view(chart));
      if (method === "POST" && parts[5] === "links") {
        const type = String(body?.linkType ?? ""), reference = String(body?.reference ?? "").trim();
        if (!["Diagnosis", "TreatmentPlan", "Encounter", "HistoryEntry"].includes(type) || reference === "" || reference.length > 100)
          return this.refuse(400, "validation_failed", "The link needs correcting. Nothing was saved.", [{ toothKey: null, site: null, field: type ? "reference" : "linkType", code: "invalid", message: "Check the link type and reference." }]);
        chart.links ??= [];
        if (!chart.links.some((l) => l.linkType === type && l.reference === reference)) chart.links.push({ linkType: type, reference, linkedByName: ACTOR, linkedAtUtc: this.now() });
        return json(200, this.view(chart));
      }
      return null;
    }
    if (parts[3] !== "sessions") return null;
    const s = this.sessions.find((x) => x.id === parts[4]);
    if (!s) return this.refuse(404, "session_not_found", "That chart session was not found.");
    if (method === "GET" && parts.length === 5) return json(200, this.viewSession(s));
    if (method !== "POST") return null;
    const action = parts[5];
    if (action === "finalize" && s.status === "Finalized") return json(200, this.view(this.charts.find((c) => c.id === s.examId)!));
    if (action === "abandon" && s.status === "Abandoned") return json(200, this.viewSession(s));
    if (s.status !== "Draft") return this.refuse(409, "session_closed", `This chart session was ${s.status.toLowerCase()}, so it can no longer be changed. Start a new session to chart again.`);
    if (typeof body?.rowVersion !== "string" || body.rowVersion === "") return this.refuse(400, "row_version_required", "The version of the chart session you are editing is required.");
    if (body.rowVersion !== `v${s.v}`) return this.conflict(s.id);
    if (action === "entries") return this.saveEntries(s, body);
    if (action === "finalize") return this.finalize(s);
    if (action === "abandon") { s.status = "Abandoned"; s.v++; s.updatedAtUtc = this.now(); return json(200, this.viewSession(s)); }
    return null;
  }

  private saveEntries(s: StoredSession, body: Record<string, unknown>): Response {
    const empty = !["readings", "teeth", "clearSites", "clearTeeth"].some((k) => Array.isArray(body[k]) && (body[k] as unknown[]).length > 0);
    if (empty) return json(200, this.viewSession(s));
    const problems = this.batchProblems(s, body);
    if (problems.length > 0) return this.refuse(400, "validation_failed", "Some entries need correcting. Nothing in this save was applied; everything entered before is as it was.", problems);
    for (const c of (body.clearSites as { toothKey: string; site: string }[] | undefined) ?? []) s.readings.delete(`${c.toothKey}:${c.site}`);
    for (const k of (body.clearTeeth as string[] | undefined) ?? []) s.teeth.delete(k);
    for (const r of (body.readings as Record<string, unknown>[] | undefined) ?? []) {
      s.readings.set(`${r.toothKey}:${r.site}`, { toothKey: String(r.toothKey), site: String(r.site), probingDepthMm: r.probingDepthMm as number, recessionMm: r.recessionMm as number, attachmentLossMm: (r.probingDepthMm as number) + (r.recessionMm as number), bleeding: r.bleeding as boolean, suppuration: (r.suppuration as boolean | null | undefined) ?? null, plaque: (r.plaque as boolean | null | undefined) ?? null });
    }
    for (const t of (body.teeth as Record<string, unknown>[] | undefined) ?? []) s.teeth.set(String(t.toothKey), { toothKey: String(t.toothKey), mobility: (t.mobility as number | null | undefined) ?? null, furcation: (t.furcation as number | null | undefined) ?? null, excluded: t.excluded === true });
    s.v++;
    s.updatedAtUtc = this.now();
    return json(200, this.viewSession(s));
  }

  private finalize(s: StoredSession): Response {
    if (s.readings.size === 0) return this.refuse(400, "validation_failed", "The chart cannot be finalized yet. Nothing was saved; the session is still open.", [{ toothKey: null, site: null, field: "readings", code: "required", message: "Enter at least one site reading before saving." }]);
    const absent = [...s.readings.values()].filter((r) => this.absentTeeth.includes(r.toothKey) && !s.teeth.get(r.toothKey)?.excluded);
    if (absent.length > 0) return this.refuse(400, "validation_failed", "The chart cannot be finalized yet. Nothing was saved; the session is still open.", absent.map((r) => ({ toothKey: r.toothKey, site: r.site, field: "toothKey", code: "tooth_absent", message: `Tooth ${r.toothKey} is recorded as missing in the odontogram, so it cannot be charted. Mark it as not charted, or correct the odontogram first.` })));
    const chart = this.addChart(s.patientId, { recordedAtUtc: this.now(), readings: [...s.readings.values()], teeth: [...s.teeth.values()] });
    s.status = "Finalized";
    s.examId = chart.id;
    s.v++;
    s.updatedAtUtc = this.now();
    return json(200, this.view(chart as StoredChart));
  }

  private saveWholeChart(patientId: string, body: Body): Response {
    const key = typeof body?.idempotencyKey === "string" ? body.idempotencyKey.trim() : "";
    const problems = [...(key === "" || key.length > 64 ? [{ toothKey: null, site: null, field: "idempotencyKey", code: "invalid", message: "Send a key of 1 to 64 characters with each save, so a retry cannot create a second chart." }] : []), ...this.problems(body?.readings)];
    if (problems.length > 0) return json(400, { error: "validation_failed", message: "The chart has entries that need correcting. Nothing was saved.", problems });
    const readings = (body!.readings as Omit<PerioReading, "attachmentLossMm">[]).map((r) => ({ ...r, attachmentLossMm: r.probingDepthMm + r.recessionMm }));
    const existing = this.charts.find((c) => c.patientId === patientId && c.key === key);
    if (existing) {
      const same = JSON.stringify(existing.readings.map(({ attachmentLossMm: _a, ...r }) => r)) === JSON.stringify(readings.map(({ attachmentLossMm: _a, ...r }) => r));
      return same ? json(200, this.view(existing)) : json(409, { error: "idempotency_key_reused", message: "That save key was already used for a different chart. Nothing was changed; save again to record this chart as a new one.", problems: [] });
    }
    const chart: StoredChart = { id: `pe-${++this.seq}`, patientId, key, recordedAtUtc: new Date(Date.UTC(2026, 9, 5, 9, this.seq)).toISOString(), recordedByName: ACTOR, readingCount: readings.length, readings, teeth: [], links: [] };
    this.charts.push(chart);
    return json(200, this.view(chart));
  }

  private view({ key: _key, ...chart }: StoredChart): PerioChart { return chart; }

  // ---------- comparison (mirrors the backend's PerioComparison) ----------

  private figures(readings: PerioReading[]): PerioFigures {
    const n = readings.length;
    const r1 = (x: number) => Math.round(x * 10 + (x >= 0 ? 1e-9 : -1e-9)) / 10;
    const assessed = readings.filter((x) => x.plaque !== null && x.plaque !== undefined);
    return {
      sites: n, teeth: new Set(readings.map((x) => x.toothKey)).size, meanDepthMm: n ? r1(readings.reduce((a, x) => a + x.probingDepthMm, 0) / n) : 0, meanAttachmentLossMm: n ? r1(readings.reduce((a, x) => a + x.attachmentLossMm, 0) / n) : 0,
      bleedingPercent: n ? Math.round((100 * readings.filter((x) => x.bleeding).length) / n) : 0, deepSites: readings.filter((x) => x.probingDepthMm >= DEEP_MM).length, veryDeepSites: readings.filter((x) => x.probingDepthMm >= VERY_DEEP_MM).length,
      suppurationSites: readings.filter((x) => x.suppuration === true).length, plaquePercent: assessed.length ? Math.round((100 * assessed.filter((x) => x.plaque === true).length) / assessed.length) : null,
    };
  }

  private compare(patientId: string, url: URL): Response {
    const examId = url.searchParams.get("currentExamId"), sessionId = url.searchParams.get("currentSessionId"), prevId = url.searchParams.get("previousExamId");
    if (!!examId === !!sessionId) return this.refuse(400, "validation_failed", "Name the chart to compare: either a saved chart or the draft being entered.");
    let current: { readings: PerioReading[]; teeth: PerioSession["teeth"]; at: string; id: string | null };
    if (examId) {
      const c = this.charts.find((x) => x.id === examId && x.patientId === patientId);
      if (!c) return this.refuse(404, "exam_not_found", "That periodontal chart was not found.");
      current = { readings: c.readings, teeth: c.teeth ?? [], at: c.recordedAtUtc, id: c.id };
    } else {
      const s = this.sessions.find((x) => x.id === sessionId && x.patientId === patientId);
      if (!s) return this.refuse(404, "session_not_found", "That chart session was not found.");
      const v = this.viewSession(s);
      current = { readings: v.readings, teeth: v.teeth, at: "9999", id: s.examId };
    }
    const previous = prevId ? this.charts.find((x) => x.id === prevId && x.patientId === patientId)
      : this.charts.filter((x) => x.patientId === patientId && x.recordedAtUtc < current.at && x.id !== current.id).sort((a, b) => b.recordedAtUtc.localeCompare(a.recordedAtUtc))[0];
    if (!previous) return prevId ? this.refuse(404, "exam_not_found", "That periodontal chart was not found.") : this.refuse(404, "no_previous_chart", "There is no earlier finalized chart to compare with.");
    const before = new Map(previous.readings.map((r) => [`${r.toothKey}:${r.site}`, r])), now = new Map(current.readings.map((r) => [`${r.toothKey}:${r.site}`, r]));
    const sites: PerioSiteChange[] = [];
    for (const tooth of ARCH_ORDER) for (const site of SITES) {
      const p = before.get(`${tooth}:${site}`), c = now.get(`${tooth}:${site}`);
      if (!p && !c) continue;
      const d = p && c ? c.probingDepthMm - p.probingDepthMm : null;
      sites.push({
        toothKey: tooth, site, previousDepthMm: p?.probingDepthMm ?? null, currentDepthMm: c?.probingDepthMm ?? null, depthChangeMm: d, previousAttachmentLossMm: p?.attachmentLossMm ?? null, currentAttachmentLossMm: c?.attachmentLossMm ?? null,
        attachmentLossChangeMm: p && c ? c.attachmentLossMm - p.attachmentLossMm : null, previousBleeding: p?.bleeding ?? null, currentBleeding: c?.bleeding ?? null,
        trend: d === null ? (p ? "OnlyPrevious" : "OnlyCurrent") : d <= -SIGNIFICANT_CHANGE_MM ? "Improved" : d >= SIGNIFICANT_CHANGE_MM ? "Worsened" : "Unchanged", currentCues: c ? cuesOf(c) : [],
      });
    }
    const matched = sites.filter((x) => x.depthChangeMm !== null);
    const stateOf = (readings: PerioReading[], teeth: PerioSession["teeth"], k: string) => (teeth.find((t) => t.toothKey === k)?.excluded ? "Excluded" : readings.some((r) => r.toothKey === k) ? "Charted" : "NotRecorded");
    const teeth: PerioToothChange[] = [];
    for (const k of ARCH_ORDER) {
      const pt = previous.teeth?.find((t) => t.toothKey === k), ct = current.teeth.find((t) => t.toothKey === k);
      const [ps, cs] = [stateOf(previous.readings, previous.teeth ?? [], k), stateOf(current.readings, current.teeth, k)];
      if (ps === cs && (pt?.mobility ?? null) === (ct?.mobility ?? null) && (pt?.furcation ?? null) === (ct?.furcation ?? null)) continue;
      teeth.push({ toothKey: k, previousState: ps, currentState: cs, previousMobility: pt?.mobility ?? null, currentMobility: ct?.mobility ?? null, previousFurcation: pt?.furcation ?? null, currentFurcation: ct?.furcation ?? null });
    }
    const mean = matched.length ? Math.round((matched.reduce((a, x) => a + x.depthChangeMm!, 0) / matched.length) * 10 + 1e-9) / 10 : 0;
    const body: PerioComparison = {
      previous: this.figures(previous.readings), current: this.figures(current.readings), matchedSites: matched.length, improved: matched.filter((x) => x.trend === "Improved").length,
      worsened: matched.filter((x) => x.trend === "Worsened").length, unchanged: matched.filter((x) => x.trend === "Unchanged").length, onlyPrevious: sites.filter((x) => x.trend === "OnlyPrevious").length,
      onlyCurrent: sites.filter((x) => x.trend === "OnlyCurrent").length, meanDepthChangeMm: mean, sites, teeth,
    };
    return json(200, body);
  }
}
