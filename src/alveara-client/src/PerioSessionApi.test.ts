import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { ApiError, isConcurrencyConflict } from "./services/authApi";
import {
  abandonPerioSession, comparePerio, finalizePerioSession, getCurrentPerioSession, getPerioChart, linkPerioChart, perioProblemsOf, savePerioEntries, startPerioSession,
} from "./services/perioApi";
import type { PerioReading } from "./services/perioApi";
import { FakeClinicalServer } from "./test/fakeClinicalServer";
import { makePatient } from "./test/fakePatientServer";

/**
 * ALV-012-C01: the typed client for chart sessions, against the in-memory fake of the API. The fake models the same shapes and rules as the backend (proven there and in the real-backend run), so this
 * proves the client sends what the API expects and reads what it returns: one shared draft, a batch applied together or not at all, a stale edit as the shared conflict, finalizing twice returning
 * one chart, links, and comparison.
 */
const A = "aaaaaaaa-0000-0000-0000-000000000001";
let server: FakeClinicalServer;
const site = (toothKey: string, s: string, probingDepthMm: number, recessionMm = 0, bleeding = false) => ({ toothKey, site: s, probingDepthMm, recessionMm, bleeding });
const reading = (toothKey: string, s: string, pd: number, rec = 0, bleeding = false): PerioReading => ({ toothKey, site: s, probingDepthMm: pd, recessionMm: rec, attachmentLossMm: pd + rec, bleeding });

beforeEach(() => {
  server = new FakeClinicalServer();
  server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" }));
  server.install();
});
afterEach(() => vi.unstubAllGlobals());

describe("starting and resuming a draft", () => {
  it("there is no draft until one is started, then the same one however often it is asked", async () => {
    expect((await getCurrentPerioSession(A)).session).toBeNull();
    const first = await startPerioSession(A);
    expect(first).toMatchObject({ status: "Draft", examId: null, next: { toothKey: "18", site: "DB" }, chartableSites: 192 });
    expect((await startPerioSession(A)).id).toBe(first.id);
    expect((await getCurrentPerioSession(A)).session?.id).toBe(first.id);
  });
});

describe("saving entries", () => {
  it("saves a tooth at a time with its grades, reads them back in site order with derived attachment loss, and moves the version", async () => {
    const s0 = await startPerioSession(A);
    const s1 = await savePerioEntries(s0, { readings: [site("16", "ML", 5, 2, true), site("16", "DB", 3), site("16", "B", 4, 1)], teeth: [{ toothKey: "16", mobility: 1, furcation: 2 }] });
    expect(s1.readings.map((r) => r.site)).toEqual(["DB", "B", "ML"]);
    expect(s1.readings.find((r) => r.site === "ML")).toMatchObject({ probingDepthMm: 5, recessionMm: 2, attachmentLossMm: 7, bleeding: true });
    expect(s1.teeth).toEqual([{ toothKey: "16", mobility: 1, furcation: 2, excluded: false }]);
    expect(s1.rowVersion).not.toBe(s0.rowVersion);
  });

  it("a refused save names every problem and changes nothing, even the version; earlier entries are still there", async () => {
    const good = await savePerioEntries(await startPerioSession(A), { readings: [site("18", "B", 3)] });
    const err = await savePerioEntries(good, { readings: [site("17", "B", 99), site("17", "MB", 3)], teeth: [{ toothKey: "11", furcation: 2 }] }).catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(perioProblemsOf(err).map((p) => p.code)).toEqual(["out_of_range", "furcation_not_applicable"]);
    const after = (await getCurrentPerioSession(A)).session!;
    expect(after.rowVersion).toBe(good.rowVersion);
    expect(after.readings.map((r) => r.toothKey)).toEqual(["18"]);                       // the valid site of the refused batch was not saved either
  });

  it("an excluded or missing tooth is skipped by next and takes no readings unless marked not charted", async () => {
    server.perio.absentTeeth = ["18"];
    let s = await startPerioSession(A);
    expect(s).toMatchObject({ absentTeeth: ["18"], next: { toothKey: "17", site: "DB" }, chartableSites: 186 });
    const err = await savePerioEntries(s, { readings: [site("18", "B", 3)] }).catch((e) => e);
    expect(perioProblemsOf(err)[0].code).toBe("tooth_absent");
    s = await savePerioEntries(s, { teeth: [{ toothKey: "18", excluded: true }] });
    expect(s.teeth[0].excluded).toBe(true);
  });

  it("a stale edit is the shared conflict, nothing is merged, and after reloading the edit goes through", async () => {
    const read = await startPerioSession(A);
    server.perio.touchSession(read.id);                                                   // someone else saved first
    const err = await savePerioEntries(read, { readings: [site("16", "B", 3)] }).catch((e) => e);
    expect(isConcurrencyConflict(err)).toBe(true);
    const fresh = (await getCurrentPerioSession(A)).session!;
    expect(fresh.readings).toEqual([]);
    expect((await savePerioEntries(fresh, { readings: [site("16", "B", 3)] })).readings).toHaveLength(1);
  });

  it("clearing a site and a tooth removes them", async () => {
    let s = await savePerioEntries(await startPerioSession(A), { readings: [site("16", "B", 3), site("16", "MB", 4)], teeth: [{ toothKey: "16", mobility: 1 }] });
    s = await savePerioEntries(s, { clearSites: [{ toothKey: "16", site: "MB" }], clearTeeth: ["16"] });
    expect(s.readings.map((r) => r.site)).toEqual(["B"]);
    expect(s.teeth).toEqual([]);
  });
});

describe("finalizing, abandoning and linking", () => {
  it("finalizing makes the chart and closes the draft; finalizing again returns the same chart; a closed draft refuses more", async () => {
    const s = await savePerioEntries(await startPerioSession(A), { readings: [site("16", "B", 3)], teeth: [{ toothKey: "17", excluded: true }] });
    const chart = await finalizePerioSession(s);
    expect(chart).toMatchObject({ readingCount: 1, teeth: [{ toothKey: "17", excluded: true }] });
    expect((await finalizePerioSession(s)).id).toBe(chart.id);
    expect((await getCurrentPerioSession(A)).session).toBeNull();
    const err = await savePerioEntries(s, { readings: [site("16", "MB", 3)] }).catch((e) => e);
    expect(err).toMatchObject({ status: 409, code: "session_closed" });
  });

  it("an empty draft cannot be finalized and stays open", async () => {
    const err = await finalizePerioSession(await startPerioSession(A)).catch((e) => e);
    expect(perioProblemsOf(err)[0].code).toBe("required");
    expect((await getCurrentPerioSession(A)).session).not.toBeNull();
  });

  it("abandoning closes the draft so a new one can start", async () => {
    const s = await startPerioSession(A);
    expect((await abandonPerioSession(s)).status).toBe("Abandoned");
    expect((await startPerioSession(A)).id).not.toBe(s.id);
  });

  it("a finalized chart can be linked once per type and reference, and the link reads back with the chart", async () => {
    const chart = await finalizePerioSession(await savePerioEntries(await startPerioSession(A), { readings: [site("16", "B", 3)] }));
    await linkPerioChart(chart.id, "Diagnosis", "dx-1");
    const again = await linkPerioChart(chart.id, "Diagnosis", " dx-1 ");
    expect(again.links).toHaveLength(1);
    expect((await getPerioChart(chart.id)).links?.[0]).toMatchObject({ linkType: "Diagnosis", reference: "dx-1" });
    expect(await linkPerioChart(chart.id, "Procedure", "x").catch((e) => e)).toMatchObject({ status: 400 });
  });
});

describe("comparison", () => {
  it("compares the draft with the latest finalized chart and a saved chart with the one before it, counting unmatched sites separately", async () => {
    server.perio.addChart(A, { recordedAtUtc: "2026-06-01T10:00:00Z", readings: [reading("16", "B", 8, 0, true)] });
    const middle = server.perio.addChart(A, { recordedAtUtc: "2026-08-01T10:00:00Z", readings: [reading("16", "B", 5, 1, true), reading("26", "DL", 4)] });
    const draft = await savePerioEntries(await startPerioSession(A), { readings: [site("16", "B", 3, 1), site("47", "B", 4, 0, true)] });
    const live = await comparePerio(A, { sessionId: draft.id });
    expect(live).toMatchObject({ matchedSites: 1, improved: 1, onlyPrevious: 1, onlyCurrent: 1 });
    expect(live.sites.find((x) => x.toothKey === "16")).toMatchObject({ previousDepthMm: 5, currentDepthMm: 3, depthChangeMm: -2, trend: "Improved" });
    const saved = await comparePerio(A, { examId: middle.id });
    expect(saved.sites.find((x) => x.toothKey === "16")).toMatchObject({ previousDepthMm: 8, currentDepthMm: 5, depthChangeMm: -3 });
    expect(saved.previous.sites).toBe(1);
  });

  it("says there is nothing to compare with when there is no earlier finalized chart", async () => {
    const only = server.perio.addChart(A, { readings: [reading("16", "B", 4)] });
    expect(await comparePerio(A, { examId: only.id }).catch((e) => e)).toMatchObject({ status: 404, code: "no_previous_chart" });
  });
});
