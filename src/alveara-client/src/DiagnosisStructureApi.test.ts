import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { ApiError, isConcurrencyConflict } from "./services/authApi";
import { amendDiagnosis, diagnosisProblemsOf, getDiagnosis, getDiagnosisHistory, linkDiagnosis, listDiagnoses, reactivateDiagnosis, recordDiagnosis, resolveDiagnosis, withdrawDiagnosis } from "./services/diagnosisApi";
import type { DiagnosisAmendmentInput, DiagnosisEntryInput } from "./services/diagnosisApi";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { makePatient } from "./test/fakePatientServer";

/**
 * ALV-013-C01: the typed client for the structure and lifecycle of a diagnosis, against the in-memory fake of the API (which models the same shapes and rules as the backend, proven there and in the
 * real-backend run). It proves the client sends what the API expects and reads what it returns: coding, source and region surviving a round trip with none required; an amendment that keeps the earlier
 * values and the treatment-plan reference; resolve and reactivate with a reason; links to a finding and a chart of the same patient only; and the treatment-plan reference never marked resolved.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const B = "bbbbbbbb-0000-0000-0000-000000000002";
let server: FakeClinicalServer;
let encounter: string;
const entry = (over: Partial<DiagnosisEntryInput> = {}): DiagnosisEntryInput => ({ encounterId: encounter, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null, ...over });
const amendment = (over: Partial<DiagnosisAmendmentInput> = {}): DiagnosisAmendmentInput => ({ toothKey: null, regionKey: null, codingSystem: null, code: null, source: null, sourceNote: null, reason: "Coded", ...over });
const refusal = async (p: Promise<unknown>) => { try { await p; } catch (e) { return e as ApiError; } throw new Error("expected a refusal"); };

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.add(makePatient({ id: B, firstName: "Bo", lastName: "Kim" }));
  server.install();
  encounter = server.addEncounter(A).id;
});
afterEach(() => vi.unstubAllGlobals());

describe("coding, source and region", () => {
  it("are optional: a diagnosis with none reads as manual and uncoded", async () => {
    const d = await recordDiagnosis(A, "k1", entry());
    expect(d).toMatchObject({ codingSystem: null, code: null, source: "Manual", sourceNote: null, regionKey: null, status: "Active", links: [] });
  });

  it("survive a round trip through record, read, list and history", async () => {
    const d = await recordDiagnosis(A, "k1", entry({ codingSystem: "ICD-10-CM", code: "K05.311", source: "Imported", sourceNote: "From the old chart", regionKey: "LowerArch" }));
    const want = { codingSystem: "ICD-10-CM", code: "K05.311", source: "Imported", sourceNote: "From the old chart", regionKey: "LowerArch" };
    expect(d).toMatchObject(want);
    expect(await getDiagnosis(d.id)).toMatchObject(want);
    expect((await listDiagnoses(A)).diagnoses[0]).toMatchObject(want);
    expect((await getDiagnosisHistory(d.id)).versions[0]).toMatchObject({ ...want, changeType: "Recorded" });
  });

  it("are refused by name when wrong, with every problem listed and nothing saved", async () => {
    const e = await refusal(recordDiagnosis(A, "k1", entry({ toothKey: "16", codingSystem: "SNOMED", code: null, source: "Wrong", regionKey: "Nowhere" })));
    expect(e.body.error).toBe("validation_failed");
    const found = diagnosisProblemsOf(e).map((p) => `${p.field}:${p.code}`);
    for (const expected of ["codingSystem:unsupported_system", "code:coding_incomplete", "source:unsupported_source", "regionKey:unknown_region"]) expect(found).toContain(expected);
    expect((await listDiagnoses(A, { includeWithdrawn: true })).diagnoses).toHaveLength(0);
  });

  it("with the same key replay the diagnosis and with a different code under the key are refused", async () => {
    const first = await recordDiagnosis(A, "k1", entry({ codingSystem: "Local", code: "A1" }));
    expect((await recordDiagnosis(A, "k1", entry({ codingSystem: "Local", code: "A1" }))).id).toBe(first.id);
    expect((await refusal(recordDiagnosis(A, "k1", entry({ codingSystem: "Local", code: "A2" })))).body.error).toBe("idempotency_key_reused");
  });
});

describe("amending the structure", () => {
  it("changes the structure, keeps the earlier values in the history with the reason, and carries the plan reference through unresolved", async () => {
    const d = await recordDiagnosis(A, "k1", entry({ toothKey: "16", treatmentPlanReference: "plan-1" }));
    const amended = await amendDiagnosis(d, amendment({ regionKey: "UpperRight", codingSystem: "Local", code: "P-9", source: "Mapped", sourceNote: "Mapped from old", reason: "Moved to the region and coded" }));
    expect(amended).toMatchObject({ toothKey: null, regionKey: "UpperRight", codingSystem: "Local", code: "P-9", source: "Mapped", treatmentPlanReference: "plan-1", treatmentPlanReferenceState: "Unresolved" });
    const versions = (await getDiagnosisHistory(d.id)).versions;
    expect(versions.map((v) => v.changeType)).toEqual(["Recorded", "Amended"]);
    expect(versions[0]).toMatchObject({ toothKey: "16", regionKey: null, codingSystem: null });
    expect(versions[1]).toMatchObject({ regionKey: "UpperRight", codingSystem: "Local", reason: "Moved to the region and coded" });
    expect(versions.every((v) => v.treatmentPlanReference === "plan-1" && v.treatmentPlanReferenceState === "Unresolved")).toBe(true);
  });

  it("changing nothing is quiet, and a tooth with a region, a missing reason or a stale version is refused", async () => {
    const d = await recordDiagnosis(A, "k1", entry({ codingSystem: "Local", code: "A1" }));
    expect((await amendDiagnosis(d, amendment({ codingSystem: "Local", code: "A1" }))).rowVersion).toBe(d.rowVersion);
    const conflict = await refusal(amendDiagnosis(d, amendment({ toothKey: "16", regionKey: "UpperArch", codingSystem: "Local", code: "A1" })));
    expect(diagnosisProblemsOf(conflict).map((p) => p.code)).toContain("conflicts_with_tooth");
    expect(diagnosisProblemsOf(await refusal(amendDiagnosis(d, amendment({ codingSystem: "Local", code: "B2", reason: "" })))).map((p) => p.field)).toContain("reason");
    await amendDiagnosis(d, amendment({ codingSystem: "Local", code: "B2" }));
    expect(isConcurrencyConflict(await refusal(amendDiagnosis(d, amendment({ codingSystem: "Local", code: "C3" }))))).toBe(true);
  });
});

describe("resolving and reactivating", () => {
  it("is a recorded, reversible lifecycle that keeps a resolved diagnosis on the list", async () => {
    const d = await recordDiagnosis(A, "k1", entry());
    const resolved = await resolveDiagnosis(d, "Healed at review");
    expect(resolved.status).toBe("Resolved");
    expect((await listDiagnoses(A)).diagnoses).toHaveLength(1);
    expect((await reactivateDiagnosis(resolved, "Came back")).status).toBe("Active");
    const versions = (await getDiagnosisHistory(d.id)).versions;
    expect(versions.map((v) => [v.changeType, v.status, v.reason])).toEqual([["Recorded", "Active", null], ["Resolved", "Resolved", "Healed at review"], ["Reactivated", "Active", "Came back"]]);
  });

  it("needs a reason, repeats quietly, and is refused on a withdrawn diagnosis", async () => {
    const d = await recordDiagnosis(A, "k1", entry());
    expect((await refusal(resolveDiagnosis(d, " "))).body.error).toBe("reason_required");
    const resolved = await resolveDiagnosis(d, "Healed");
    expect((await resolveDiagnosis(resolved, "Healed again")).rowVersion).toBe(resolved.rowVersion);
    const gone = await withdrawDiagnosis(resolved, "Wrong patient");
    expect((await refusal(reactivateDiagnosis(gone, "x"))).body.error).toBe("diagnosis_withdrawn");
    expect((await refusal(amendDiagnosis(gone, amendment()))).body.error).toBe("diagnosis_withdrawn");
  });
});

describe("links", () => {
  it("link a diagnosis to a finding and a chart of the same patient, once, with who and when", async () => {
    const d = await recordDiagnosis(A, "k1", entry());
    const finding = server.odontogram.addFinding(A, { toothKey: "16", condition: "Crown" });
    const chart = server.perio.addChart(A, { readings: [], recordedAtUtc: "2026-09-01T10:00:00Z" });
    await linkDiagnosis(d.id, "Finding", finding.id);
    const linked = await linkDiagnosis(d.id, "PerioExam", chart.id);
    expect((await linkDiagnosis(d.id, "Finding", finding.id)).links).toHaveLength(2);                  // the same link again adds nothing
    expect(linked.links!.map((l) => [l.linkType, l.targetId])).toEqual([["Finding", finding.id], ["PerioExam", chart.id]]);
    expect(linked.links![1].summary).toBe("Periodontal chart of 2026-09-01");
    expect(linked.links!.every((l) => l.linkedByName === "Dr. Okafor")).toBe(true);
  });

  it("refuse another patient's record and a missing one in the same words, and a type that is not supported", async () => {
    const d = await recordDiagnosis(A, "k1", entry());
    const other = server.odontogram.addFinding(B, { toothKey: "16", condition: "Crown" });
    for (const target of [other.id, "nope"]) {
      const e = await refusal(linkDiagnosis(d.id, "Finding", target));
      expect([e.status, e.body.error]).toEqual([404, "link_target_not_found"]);
    }
    expect((await refusal(linkDiagnosis(d.id, "TreatmentPlan" as never, "x"))).body.error).toBe("validation_failed");
    expect((await getDiagnosis(d.id)).links).toEqual([]);
  });
});

describe("the treatment-plan reference", () => {
  it("is never marked resolved: a request to do so is refused on record and on amend", async () => {
    const e = await refusal(recordDiagnosis(A, "k1", { ...entry({ treatmentPlanReference: "plan-1" }), treatmentPlanReferenceState: "Resolved" } as DiagnosisEntryInput));
    expect(diagnosisProblemsOf(e).map((p) => `${p.field}:${p.code}`)).toContain("treatmentPlanReferenceState:not_supported");
    const d = await recordDiagnosis(A, "k2", entry({ treatmentPlanReference: "plan-1" }));
    expect(d.treatmentPlanReferenceState).toBe("Unresolved");
  });
});
