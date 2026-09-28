import { describe, it, expect, vi, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { useSystemStatus } from "./useSystemStatus";

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

const healthyStatus = {
  appVersion: "1.0.0.0",
  localServerReachable: true,
  database: { reachable: true },
  backgroundRunner: { status: "healthy" as const, lastPollUtc: "2026-01-01T00:00:00Z" },
};

describe("useSystemStatus", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("reports loaded on a healthy, well-shaped response", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(healthyStatus)));
    const { result } = renderHook(() => useSystemStatus());
    await waitFor(() => expect(result.current.kind).toBe("loaded"));
  });

  it("reports error (never stale) when the very first poll fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("network error")));
    const { result } = renderHook(() => useSystemStatus());
    await waitFor(() => expect(result.current.kind).toBe("error"));
  });

  it("rejects a response with an unexpected shape rather than accepting it as healthy", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse({ unexpected: true })));
    const { result } = renderHook(() => useSystemStatus());
    await waitFor(() => expect(result.current.kind).toBe("error"));
  });

  it("transitions to stale (carrying the last good status) when a later poll fails after a prior success", async () => {
    vi.useFakeTimers();
    try {
      const fetchMock = vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(healthyStatus))
        .mockRejectedValue(new Error("network error"));
      vi.stubGlobal("fetch", fetchMock);

      const { result } = renderHook(() => useSystemStatus());

      await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(result.current.kind).toBe("loaded");

      await act(async () => {
        await vi.advanceTimersByTimeAsync(15_000); // past the poll interval
      });

      expect(result.current.kind).toBe("stale");
      if (result.current.kind === "stale") {
        expect(result.current.lastGoodStatus).toEqual(healthyStatus);
        expect(result.current.lastConfirmedAtUtc).toBeTruthy();
      }
    } finally {
      vi.useRealTimers();
    }
  });

  it("treats a hung request as a failure via its own timeout, not an indefinite loading state", async () => {
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
      const { result } = renderHook(() => useSystemStatus());

      await act(async () => {
        await vi.advanceTimersByTimeAsync(5_000); // past the 4s request timeout
      });

      expect(result.current.kind).toBe("error");
    } finally {
      vi.useRealTimers();
    }
  });
});
