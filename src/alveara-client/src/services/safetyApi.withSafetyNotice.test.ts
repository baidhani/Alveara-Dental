import { afterEach, describe, expect, it } from "vitest";
import { SAFETY_CHANGED_EVENT, withSafetyNotice } from "./safetyApi";

// ALV-N022 A1 (OD-05): one banner, refreshed after every clinical write - a saved write tells the banners for that patient to refetch; a refused write does not.
describe("withSafetyNotice", () => {
  const seen: string[] = [];
  const listener = (e: Event) => seen.push((e as CustomEvent<{ patientId: string }>).detail.patientId);
  afterEach(() => { window.removeEventListener(SAFETY_CHANGED_EVENT, listener); seen.length = 0; });

  it("announces the patient once the write is saved and returns the server's answer", async () => {
    window.addEventListener(SAFETY_CHANGED_EVENT, listener);
    const saved = { patientId: "p1", id: "e1" };
    await expect(withSafetyNotice(Promise.resolve(saved))).resolves.toBe(saved);
    expect(seen).toEqual(["p1"]);
  });

  it("announces nothing when the write is refused, and passes the refusal on", async () => {
    window.addEventListener(SAFETY_CHANGED_EVENT, listener);
    const refusal = new Error("duplicate_item");
    await expect(withSafetyNotice(Promise.reject(refusal))).rejects.toBe(refusal);
    expect(seen).toEqual([]);
  });

  it("does not announce before the write settles", async () => {
    window.addEventListener(SAFETY_CHANGED_EVENT, listener);
    let finish: (v: { patientId: string }) => void = () => {};
    const pending = withSafetyNotice(new Promise<{ patientId: string }>((r) => { finish = r; }));
    await Promise.resolve();
    expect(seen).toEqual([]);
    finish({ patientId: "p2" });
    await pending;
    expect(seen).toEqual(["p2"]);
  });
});
