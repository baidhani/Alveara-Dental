import { describe, it, expect, vi, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { useConnectionStatus } from "./useConnectionStatus";

function jsonResponse(body: unknown, init?: ResponseInit) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
    ...init,
  });
}

function htmlFallbackResponse() {
  // Reproduces exactly what N001-R01-01 found: Vite's dev-server HTML
  // fallback answering a relative "/api/health" request with a real 200
  // when there is no proxy/listener behind it.
  return new Response("<!doctype html><html><body>vite</body></html>", {
    status: 200,
    headers: { "Content-Type": "text/html" },
  });
}

describe("useConnectionStatus", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("reports connected when the API returns the real health payload", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ status: "ok" })));
    const { result } = renderHook(() => useConnectionStatus());
    await waitFor(() => expect(result.current).toBe("connected"));
  });

  it("reports disconnected when fetch rejects (server down)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network error")));
    const { result } = renderHook(() => useConnectionStatus());
    await waitFor(() => expect(result.current).toBe("disconnected"));
  });

  it("reports disconnected on a 200 HTML fallback response, not a real API health payload", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(htmlFallbackResponse()));
    const { result } = renderHook(() => useConnectionStatus());
    await waitFor(() => expect(result.current).toBe("disconnected"));
  });

  it("reports disconnected on a 200 JSON response with an unexpected payload shape", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ status: "degraded" })));
    const { result } = renderHook(() => useConnectionStatus());
    await waitFor(() => expect(result.current).toBe("disconnected"));
  });

  it("reports disconnected when the request times out", async () => {
    vi.useFakeTimers();
    try {
      vi.stubGlobal(
        "fetch",
        vi.fn().mockImplementation(
          (_url: string, init?: { signal?: AbortSignal }) =>
            new Promise((_resolve, reject) => {
              init?.signal?.addEventListener("abort", () => reject(new Error("aborted")));
            })
        )
      );
      const { result } = renderHook(() => useConnectionStatus());

      await act(async () => {
        await vi.advanceTimersByTimeAsync(5_000); // past the 4s request timeout
      });

      expect(result.current).toBe("disconnected");
    } finally {
      vi.useRealTimers();
    }
  });

  it("recovers to connected on the next poll after the API comes back up", async () => {
    vi.useFakeTimers();
    try {
      const fetchMock = vi
        .fn()
        .mockResolvedValueOnce(htmlFallbackResponse())
        .mockResolvedValue(jsonResponse({ status: "ok" }));
      vi.stubGlobal("fetch", fetchMock);

      const { result } = renderHook(() => useConnectionStatus());

      // Flush the microtask queue so the immediate on-mount check resolves,
      // without advancing macrotask timers yet.
      await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(result.current).toBe("disconnected");

      await act(async () => {
        await vi.advanceTimersByTimeAsync(15_000); // past the poll interval
      });

      expect(result.current).toBe("connected");
    } finally {
      vi.useRealTimers();
    }
  });
});
