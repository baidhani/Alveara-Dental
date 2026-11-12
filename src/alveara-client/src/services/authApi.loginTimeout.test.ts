import { afterEach, describe, expect, it, vi } from "vitest";
import { LOGIN_TIMEOUT_MS, login } from "./authApi";
import { classifyLoginFailure } from "../pages/loginOutcome";

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

/** A sign-in request the server never answers; it rejects only when its signal is aborted, as a real fetch does. */
function hangingFetch() {
  const signals: AbortSignal[] = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((_url: string, init?: RequestInit) => new Promise<Response>((_resolve, reject) => {
    const signal = init!.signal!;
    signals.push(signal);
    signal.addEventListener("abort", () => reject(Object.assign(new Error("The operation was aborted."), { name: "AbortError" })));
  })));
  return signals;
}

describe("login timeout (ALV-N020, F-014)", () => {
  it("a sign-in the server never answers is aborted at 15 s, not before, and is reported as the server being unreachable", async () => {
    vi.useFakeTimers();
    const signals = hangingFetch();
    const attempt = login("dentist1", "password-1234");
    const settled = attempt.then(() => "resolved", (err: unknown) => err);

    await vi.advanceTimersByTimeAsync(LOGIN_TIMEOUT_MS - 1);
    expect(signals[0].aborted).toBe(false);                                                      // still waiting just before the limit

    await vi.advanceTimersByTimeAsync(1);
    expect(signals[0].aborted).toBe(true);
    const err = await settled;
    expect(err).toMatchObject({ name: "AbortError" });
    expect(classifyLoginFailure(err).kind).toBe("server-unavailable");                            // the person sees "the server cannot be reached"
  });

  it("a sign-in that answers in time leaves no timer behind", async () => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "a1", username: "dentist1", role: "Dentist" }), { status: 200, headers: { "Content-Type": "application/json" } })));
    await login("dentist1", "password-1234");
    expect(vi.getTimerCount()).toBe(0);
  });
});
