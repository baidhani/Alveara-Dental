import { describe, it, expect, vi, afterEach } from "vitest";
import { StrictMode, useEffect, useLayoutEffect } from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import { PatientContextProvider } from "./PatientContext";
import { usePatientContext } from "./patientContextStore";

/** ALV-003-C01: the patient-in-context state machine, in isolation: a switch drops the old patient at once and a late answer never resurrects it. */
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const detail = (id: string, name: string) => ({ id, firstName: name, lastName: "Test", dateOfBirth: "1990-01-01", age: 36, isActive: true, rowVersion: "v1", guarantor: null, guaranteeFor: [], household: null });

type Ctx = ReturnType<typeof usePatientContext>;
const holder: { current: Ctx | null } = { current: null };
function Probe() {
  const ctx = usePatientContext();
  useEffect(() => {
    holder.current = ctx;
  });
  const s = ctx.state;
  return <p data-testid="state">{s.kind === "loaded" ? `loaded:${s.patient.firstName}` : s.kind === "none" ? "none" : `${s.kind}:${s.patientId}`}</p>;
}
const state = () => screen.getByTestId("state").textContent;

/** A fetch whose answers the test releases by hand, so request ordering is under its control. */
function controlledFetch() {
  const pending: { id: string; signal?: AbortSignal | null; resolve: (r: Response) => void }[] = [];
  vi.stubGlobal("fetch", vi.fn((url: string, init?: RequestInit) => new Promise<Response>((resolve) => { pending.push({ id: String(url).split("/").pop()!, signal: init?.signal, resolve }); })));
  return pending;
}

afterEach(() => vi.unstubAllGlobals());

describe("PatientContextProvider", () => {
  it("starts with no patient, loads one on select, and drops it on clear", async () => {
    const pending = controlledFetch();
    render(<PatientContextProvider><Probe /></PatientContextProvider>);
    expect(state()).toBe("none");

    act(() => holder.current!.selectPatient("a"));
    expect(state()).toBe("loading:a");
    await act(async () => pending[0].resolve(json(200, detail("a", "Ann"))));
    expect(state()).toBe("loaded:Ann");

    act(() => holder.current!.clearPatient());
    expect(state()).toBe("none");
  });

  it("selecting a different patient drops the previous one immediately, before the new one has loaded", async () => {
    const pending = controlledFetch();
    render(<PatientContextProvider><Probe /></PatientContextProvider>);
    act(() => holder.current!.selectPatient("a"));
    await act(async () => pending[0].resolve(json(200, detail("a", "Ann"))));
    expect(state()).toBe("loaded:Ann");

    act(() => holder.current!.selectPatient("b"));

    expect(state()).toBe("loading:b"); // no frame where Ann is still the patient in context
  });

  it("ignores and aborts an older request that answers after a newer selection", async () => {
    const pending = controlledFetch();
    render(<PatientContextProvider><Probe /></PatientContextProvider>);
    act(() => holder.current!.selectPatient("a"));
    act(() => holder.current!.selectPatient("b"));
    expect(pending[0].signal?.aborted).toBe(true); // the request for Ann was cancelled

    await act(async () => pending[1].resolve(json(200, detail("b", "Ben"))));
    await act(async () => pending[0].resolve(json(200, detail("a", "Ann")))); // answers late anyway
    expect(state()).toBe("loaded:Ben");
  });

  it("selecting the patient already in context does not refetch", async () => {
    const pending = controlledFetch();
    render(<PatientContextProvider><Probe /></PatientContextProvider>);
    act(() => holder.current!.selectPatient("a"));
    await act(async () => pending[0].resolve(json(200, detail("a", "Ann"))));
    act(() => holder.current!.selectPatient("a"));
    expect(pending).toHaveLength(1);
  });

  it("clearing while a load is in flight means the late answer is never shown", async () => {
    const pending = controlledFetch();
    render(<PatientContextProvider><Probe /></PatientContextProvider>);
    act(() => holder.current!.selectPatient("a"));
    act(() => holder.current!.clearPatient());
    await act(async () => pending[0].resolve(json(200, detail("a", "Ann"))));
    expect(state()).toBe("none");
  });

  it.each([
    [404, "not-found:a"],
    [403, "denied:a"],
    [500, "error:a"],
  ])("a %i response becomes the %s state", async (status, expected) => {
    const pending = controlledFetch();
    render(<PatientContextProvider><Probe /></PatientContextProvider>);
    act(() => holder.current!.selectPatient("a"));
    await act(async () => pending[0].resolve(json(status, { error: "x" })));
    expect(state()).toBe(expected);
  });

  it("reload re-reads the same patient without blanking what is shown, then shows the fresh version", async () => {
    const pending = controlledFetch();
    render(<PatientContextProvider><Probe /></PatientContextProvider>);
    act(() => holder.current!.selectPatient("a"));
    await act(async () => pending[0].resolve(json(200, detail("a", "Ann"))));

    act(() => holder.current!.reload());
    expect(state()).toBe("loaded:Ann"); // still showing the patient while the refresh is in flight
    await act(async () => pending[1].resolve(json(200, detail("a", "Annette"))));
    await waitFor(() => expect(state()).toBe("loaded:Annette"));
  });

  it("still loads the patient under React StrictMode, which mounts, unmounts and remounts (found by the real-browser run)", async () => {
    const pending = controlledFetch();
    function Selecting({ id }: { id: string }) {
      const { selectPatient } = usePatientContext();
      useLayoutEffect(() => {
        selectPatient(id); // exactly what the patient workspace does on mount
      }, [id, selectPatient]);
      return <Probe />;
    }
    render(<StrictMode><PatientContextProvider><Selecting id="a" /></PatientContextProvider></StrictMode>);

    // StrictMode's simulated unmount aborted the first request; the remount must have asked again
    const live = pending.filter((p) => !p.signal?.aborted);
    expect(live.length).toBeGreaterThan(0);
    await act(async () => live[live.length - 1].resolve(json(200, detail("a", "Ann"))));
    expect(state()).toBe("loaded:Ann");
  });

  it("a patient can be selected again after the provider unmounted and remounted", async () => {
    const pending = controlledFetch();
    const first = render(<PatientContextProvider><Probe /></PatientContextProvider>);
    act(() => holder.current!.selectPatient("a"));
    first.unmount();
    expect(pending[0].signal?.aborted).toBe(true);

    render(<PatientContextProvider><Probe /></PatientContextProvider>);
    act(() => holder.current!.selectPatient("a"));
    expect(pending).toHaveLength(2); // asked again rather than believing the aborted load was still in flight
    await act(async () => pending[1].resolve(json(200, detail("a", "Ann"))));
    expect(state()).toBe("loaded:Ann");
  });

  it("using the hook outside the provider fails loudly", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => render(<Probe />)).toThrow(/PatientContextProvider/);
    spy.mockRestore();
  });
});
