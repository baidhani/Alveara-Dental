import { json } from "./fakePatientServer";
import type { Chart, Finding } from "../services/odontogramApi";

/**
 * STORY-006: an in-memory stand-in for the odontogram API (Controllers/OdontogramController). It models the response SHAPES the chart renders. The real rules (the 52 keys, which
 * surfaces exist on which teeth, the lifecycle, withdrawal, the audit trail) are proven by the backend tests and the real-backend walkthrough; this exists so the UI's handling of
 * each outcome can be tested in isolation. A patient with nothing seeded has an EMPTY chart - nothing is invented.
 */
const ACTOR = "Dr. Okafor";

export class FakeOdontogramStore {
  findings = new Map<string, Finding & { patientId: string }>();
  private seq = 0;

  addFinding(patientId: string, over: Partial<Finding> & { toothKey: string }): Finding {
    const f: Finding & { patientId: string } = {
      id: over.id ?? `fd-${++this.seq}`, patientId, toothKey: over.toothKey, surface: over.surface ?? null, condition: over.condition ?? "Crown", state: over.state ?? "Existing", status: over.status ?? "Active",
      recordedByName: over.recordedByName ?? ACTOR, recordedAtUtc: over.recordedAtUtc ?? "2026-09-01T10:00:00Z", updatedByName: over.updatedByName ?? null, updatedAtUtc: over.updatedAtUtc ?? null, rowVersion: over.rowVersion ?? "AAAAAAAAB9E=",
    };
    this.findings.set(f.id, f);
    return f;
  }

  chart(patientId: string): Chart {
    const findings = [...this.findings.values()].filter((f) => f.patientId === patientId && f.status === "Active")
      .map(({ patientId: _p, ...f }) => f).sort((a, b) => a.toothKey.localeCompare(b.toothKey) || (a.surface ?? "").localeCompare(b.surface ?? ""));
    return { patientId, findings };
  }

  route(path: string, method: string, _body: Record<string, unknown> | null): Response | null {
    const parts = path.split("/");
    if (parts[2] === "patients" && parts[4] === "odontogram" && method === "GET" && parts.length === 5) return json(200, this.chart(parts[3]));
    return null;
  }
}
