import { json } from "./fakePatientServer";
import type { PerioChart, PerioProblem, PerioReading } from "../services/perioApi";

/**
 * STORY-012: an in-memory stand-in for the periodontal charting API (Controllers/PerioController): the response SHAPES, the server's validation (every problem listed together, nothing
 * partial saved) and the idempotent save (the same key and readings return the chart already saved; the same key with different readings is a 409). The real rules are proven by the
 * backend tests and the real-backend walkthrough; this exists so the UI's handling of each outcome can be tested in isolation.
 */
const PERMANENT = ["1", "2", "3", "4"].flatMap((q) => [1, 2, 3, 4, 5, 6, 7, 8].map((p) => `${q}${p}`));
const PRIMARY = ["5", "6", "7", "8"].flatMap((q) => [1, 2, 3, 4, 5].map((p) => `${q}${p}`));
const SITES = ["DB", "B", "MB", "DL", "L", "ML"];
const ACTOR = "Dr. Okafor";

interface StoredChart extends PerioChart { key: string }

export class FakePerioStore {
  charts: StoredChart[] = [];
  private seq = 0;

  /** A chart saved earlier (for history tests). */
  addChart(patientId: string, over: Partial<PerioChart> & { readings: PerioReading[] }): PerioChart {
    const chart: StoredChart = {
      id: over.id ?? `pe-${++this.seq}`, patientId, recordedAtUtc: over.recordedAtUtc ?? "2026-09-01T10:00:00Z", recordedByName: over.recordedByName ?? ACTOR,
      readingCount: over.readings.length, readings: over.readings, key: `seed-${this.seq}`,
    };
    this.charts.push(chart);
    return chart;
  }

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

  route(path: string, method: string, body: Record<string, unknown> | null): Response | null {
    const parts = path.split("/");
    // /api/patients/{id}/periodontal/charts
    if (!(parts[2] === "patients" && parts[4] === "periodontal" && parts[5] === "charts")) return null;
    const patientId = parts[3];
    if (method === "GET") return json(200, { patientId, exams: this.charts.filter((c) => c.patientId === patientId).sort((a, b) => b.recordedAtUtc.localeCompare(a.recordedAtUtc)).map(({ key: _key, ...c }) => c) });
    if (method !== "POST") return null;
    const key = typeof body?.idempotencyKey === "string" ? body.idempotencyKey.trim() : "";
    const problems = [...(key === "" || key.length > 64 ? [{ toothKey: null, site: null, field: "idempotencyKey", code: "invalid", message: "Send a key of 1 to 64 characters with each save, so a retry cannot create a second chart." }] : []), ...this.problems(body?.readings)];
    if (problems.length > 0) return json(400, { error: "validation_failed", message: "The chart has entries that need correcting. Nothing was saved.", problems });
    const readings = (body!.readings as Omit<PerioReading, "attachmentLossMm">[]).map((r) => ({ ...r, attachmentLossMm: r.probingDepthMm + r.recessionMm }));
    const existing = this.charts.find((c) => c.patientId === patientId && c.key === key);
    if (existing) {
      const same = JSON.stringify(existing.readings.map(({ attachmentLossMm: _a, ...r }) => r)) === JSON.stringify(readings.map(({ attachmentLossMm: _a, ...r }) => r));
      return same ? json(200, this.view(existing)) : json(409, { error: "idempotency_key_reused", message: "That save key was already used for a different chart. Nothing was changed; save again to record this chart as a new one.", problems: [] });
    }
    const chart: StoredChart = { id: `pe-${++this.seq}`, patientId, key, recordedAtUtc: new Date(Date.UTC(2026, 9, 5, 9, this.seq)).toISOString(), recordedByName: ACTOR, readingCount: readings.length, readings };
    this.charts.push(chart);
    return json(200, this.view(chart));
  }

  private view({ key: _key, ...chart }: StoredChart): PerioChart { return chart; }
}
