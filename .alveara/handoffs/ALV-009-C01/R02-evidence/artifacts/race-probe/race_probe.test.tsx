import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { act, render, screen, fireEvent } from "@testing-library/react";
import { App } from "./App";
import { FakePatientServer, makePatient } from "./test/fakePatientServer";

const A = "aaaaaaaa-0000-0000-0000-000000000001";
let server: FakePatientServer;
beforeEach(() => { server = new FakePatientServer(); server.add(makePatient({ id: A, firstName: "Ann", lastName: "Lee" })); server.install(); });
afterEach(() => vi.unstubAllGlobals());

// probe: edit the City field in the very tick the panel is committed, before React has flushed the mount effect that re-seeds the form
it("probe: an edit made before the mount effect has run is overwritten by the re-seed", async () => {
  window.history.pushState({}, "", `/patients/${A}`);
  let edited = false;
  const observer = new MutationObserver(() => {
    const el = document.querySelector<HTMLInputElement>('input[id*="city"], input[name="city"]') ?? (screen.queryByLabelText("City *") as HTMLInputElement | null);
    if (el && !edited) { edited = true; fireEvent.change(el, { target: { value: "" } }); }
  });
  observer.observe(document.body, { childList: true, subtree: true });
  render(<App />);
  const city = (await screen.findByLabelText("City *")) as HTMLInputElement;
  await act(async () => { await new Promise((r) => setTimeout(r, 100)); });
  observer.disconnect();
  console.log("PROBE value after settle:", JSON.stringify(city.value), "edited:", edited);
  expect(edited).toBe(true);
  expect(city.value).toBe("");
});

// the fix: the same edit, made after the pending effects were flushed the way the corrected test does it, is kept
it("probe: an edit made after the pending effects were flushed is kept", async () => {
  window.history.pushState({}, "", `/patients/${A}`);
  render(<App />);
  const city = (await screen.findByLabelText("City *")) as HTMLInputElement;
  await act(async () => {});
  fireEvent.change(city, { target: { value: "" } });
  await act(async () => { await new Promise((r) => setTimeout(r, 100)); });
  expect(city.value).toBe("");
});
