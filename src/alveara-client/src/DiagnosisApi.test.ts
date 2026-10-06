import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { ApiError, isConcurrencyConflict } from "./services/authApi";
import { correctDiagnosis, diagnosisProblemsOf, getDiagnosis, getDiagnosisHistory, listDiagnoses, recordDiagnosis, withdrawDiagnosis } from "./services/diagnosisApi";
import type { DiagnosisEntryInput } from "./services/diagnosisApi";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { makePatient } from "./test/fakePatientServer";

/**
 * STORY-013: the typed client for diagnoses, against the in-memory fake of the API. The fake models the same shapes and rules as the backend (proven there and in the real-backend run), so this proves
 * the client sends what the API expects and reads what it returns: a diagnosis linked to its patient and encounter, a refused entry naming every problem, the treatment-plan forward reference kept unless
 * explicitly replaced or cleared and always shown as unresolved, a stale change as the shared conflict, and repeats that change nothing.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
const B = "bbbbbbbb-0000-0000-0000-000000000002";
let server: FakeClinicalServer;
let encounter: string;
let boEncounter: string;
const entry = (over: Partial<DiagnosisEntryInput> = {}): DiagnosisEntryInput => ({ encounterId: encounter, label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null, ...over });

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.add(makePatient({ id: B, firstName: "Bo", lastName: "Kim" }));
  server.install();
  encounter = server.addEncounter(A).id;
  boEncounter = server.addEncounter(B).id;
});
afterEach(() => vi.unstubAllGlobals());

describe("recording", () => {
  it("saves a diagnosis linked to the patient and the encounter, with the plan reference normalized and shown as unresolved", async () => {
    const d = await recordDiagnosis(A, "k1", entry({ label: "  Caries   on the occlusal surface ", toothKey: "16", notes: "Sensitive.", treatmentPlanReference: "  plan-2026-07 " }));
    expect(d).toMatchObject({ patientId: A, encounterId: encounter, label: "Caries on the occlusal surface", toothKey: "16", treatmentPlanReference: "plan-2026-07", treatmentPlanReferenceState: "Unresolved", status: "Active", recordedByName: "Dr. Okafor" });
  });

  it("says nothing about a plan when there is no reference", async () => {
    const d = await recordDiagnosis(A, "k1", entry());
    expect([d.treatmentPlanReference, d.treatmentPlanReferenceState]).toEqual([null, null]);
  });

  it("refuses incorrect data naming every problem, and saves nothing", async () => {
    const err = await recordDiagnosis(A, "k1", entry({ label: "", toothKey: "19", notes: "bell\u0007", treatmentPlanReference: "   " })).catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err).toMatchObject({ status: 400, code: "validation_failed" });
    expect(diagnosisProblemsOf(err).map((p) => `${p.field}:${p.code}`)).toEqual(["label:required", "toothKey:unknown_tooth", "notes:invalid_characters", "treatmentPlanReference:blank"]);
    expect((await listDiagnoses(A)).diagnoses).toEqual([]);
  });

  it("refuses an encounter that is another patient's or does not exist, the same way", async () => {
    for (const id of [boEncounter, "00000000-0000-0000-0000-00000000dead"]) {
      const err = await recordDiagnosis(A, `k-${id}`, entry({ encounterId: id })).catch((e) => e);
      expect(err).toMatchObject({ status: 404, code: "encounter_not_found" });
    }
    expect((await listDiagnoses(A)).diagnoses).toEqual([]);
  });

  it("returns the same diagnosis for the same key and entry, and refuses the same key with a different entry", async () => {
    const first = await recordDiagnosis(A, "visit-1", entry({ treatmentPlanReference: "plan-7" }));
    expect((await recordDiagnosis(A, "visit-1", entry({ label: "  Chronic periodontitis ", treatmentPlanReference: " plan-7 " }))).id).toBe(first.id);
    expect(await recordDiagnosis(A, "visit-1", entry({ label: "Something else" })).catch((e) => e)).toMatchObject({ status: 409, code: "idempotency_key_reused" });
    expect((await listDiagnoses(A)).diagnoses).toHaveLength(1);
  });
});

describe("correcting", () => {
  it("changes what was corrected and keeps the treatment-plan reference when it is left alone, with the reason in the history", async () => {
    const d = await recordDiagnosis(A, "k1", entry({ label: "Caries", toothKey: "16", treatmentPlanReference: "plan-7" }));
    const c = await correctDiagnosis(d, { label: "Deep caries", toothKey: "17", notes: null, treatmentPlanReference: null, clearTreatmentPlanReference: false, reason: "Wrong tooth" });
    expect(c).toMatchObject({ label: "Deep caries", toothKey: "17", treatmentPlanReference: "plan-7", treatmentPlanReferenceState: "Unresolved", updatedByName: "Dr. Okafor" });
    const h = (await getDiagnosisHistory(d.id)).versions;
    expect(h.map((v) => [v.changeType, v.label, v.reason])).toEqual([["Recorded", "Caries", null], ["Corrected", "Deep caries", "Wrong tooth"]]);
    expect(h.every((v) => v.treatmentPlanReference === "plan-7")).toBe(true);
  });

  it("replaces the reference only when given one and removes it only by an explicit clear, each kept in the history", async () => {
    const d = await recordDiagnosis(A, "k1", entry({ treatmentPlanReference: "plan-7" }));
    const base = { label: "Chronic periodontitis", toothKey: null, notes: null };
    const replaced = await correctDiagnosis(d, { ...base, treatmentPlanReference: " plan-8 ", clearTreatmentPlanReference: false, reason: "Wrong plan" });
    expect(replaced.treatmentPlanReference).toBe("plan-8");
    const both = await correctDiagnosis(replaced, { ...base, treatmentPlanReference: "plan-9", clearTreatmentPlanReference: true, reason: "x" }).catch((e) => e);
    expect(diagnosisProblemsOf(both).map((p) => p.code)).toEqual(["conflict"]);
    const cleared = await correctDiagnosis(replaced, { ...base, treatmentPlanReference: null, clearTreatmentPlanReference: true, reason: "Entered by mistake" });
    expect([cleared.treatmentPlanReference, cleared.treatmentPlanReferenceState]).toEqual([null, null]);
    expect((await getDiagnosisHistory(d.id)).versions.map((v) => v.treatmentPlanReference)).toEqual(["plan-7", "plan-8", null]);
  });

  it("needs a reason and refuses bad data naming every problem, changing nothing", async () => {
    const d = await recordDiagnosis(A, "k1", entry());
    const err = await correctDiagnosis(d, { label: "", toothKey: "19", notes: null, treatmentPlanReference: null, clearTreatmentPlanReference: false, reason: "  " }).catch((e) => e);
    expect(diagnosisProblemsOf(err).map((p) => `${p.field}:${p.code}`)).toEqual(["reason:required", "label:required", "toothKey:unknown_tooth"]);
    expect((await getDiagnosis(d.id)).rowVersion).toBe(d.rowVersion);
  });

  it("is quiet when nothing changes, and a stale correction is the shared conflict with nothing merged", async () => {
    const d = await recordDiagnosis(A, "k1", entry());
    const same = await correctDiagnosis(d, { label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null, clearTreatmentPlanReference: false, reason: "r" });
    expect(same.rowVersion).toBe(d.rowVersion);
    server.diagnoses.touch(d.id);                                                                  // someone else changed it first
    const err = await correctDiagnosis(d, { label: "Mine", toothKey: null, notes: null, treatmentPlanReference: null, clearTreatmentPlanReference: false, reason: "r" }).catch((e) => e);
    expect(isConcurrencyConflict(err)).toBe(true);
    expect((await getDiagnosis(d.id)).label).toBe("Chronic periodontitis");
  });
});

describe("withdrawing and reading", () => {
  it("withdraws with a reason, keeps the reference, is quiet when repeated, and refuses a correction afterwards", async () => {
    const d = await recordDiagnosis(A, "k1", entry({ treatmentPlanReference: "plan-7" }));
    expect(await withdrawDiagnosis(d, "  ").catch((e) => e)).toMatchObject({ status: 400, code: "reason_required" });
    const w = await withdrawDiagnosis(d, "Entered on the wrong patient");
    expect(w).toMatchObject({ status: "Withdrawn", withdrawnReason: "Entered on the wrong patient", treatmentPlanReference: "plan-7", treatmentPlanReferenceState: "Unresolved" });
    expect((await withdrawDiagnosis(d, "again")).withdrawnReason).toBe("Entered on the wrong patient");
    const err = await correctDiagnosis(w, { label: "x", toothKey: null, notes: null, treatmentPlanReference: null, clearTreatmentPlanReference: false, reason: "r" }).catch((e) => e);
    expect(err).toMatchObject({ status: 409, code: "diagnosis_withdrawn" });
  });

  it("lists a patient's diagnoses newest first, by encounter, with withdrawn ones only on request", async () => {
    const second = server.addEncounter(A).id;
    server.diagnoses.add(A, encounter, { label: "First" });
    const b = server.diagnoses.add(A, second, { label: "Second" });
    server.diagnoses.add(A, second, { label: "Third" });
    server.diagnoses.add(B, boEncounter, { label: "Bo's" });
    await withdrawDiagnosis(b, "Wrong patient");
    expect((await listDiagnoses(A)).diagnoses.map((d) => d.label)).toEqual(["Third", "First"]);
    expect((await listDiagnoses(A, { includeWithdrawn: true })).diagnoses.map((d) => d.label)).toEqual(["Third", "Second", "First"]);
    expect((await listDiagnoses(A, { encounterId: second })).diagnoses.map((d) => d.label)).toEqual(["Third"]);
    expect((await listDiagnoses(B)).diagnoses.map((d) => d.label)).toEqual(["Bo's"]);
  });

  it("answers an unknown diagnosis with a 404", async () => {
    expect(await getDiagnosis("dg-nope").catch((e) => e)).toMatchObject({ status: 404, code: "diagnosis_not_found" });
  });
});
