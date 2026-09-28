import { useEffect, useRef, useState } from "react";

export type ConnectionStatus = "checking" | "connected" | "disconnected";

const HEALTH_ENDPOINT = "/api/health";
const POLL_INTERVAL_MS = 15_000;
const REQUEST_TIMEOUT_MS = 4_000;

interface HealthPayload {
  status?: unknown;
}

function isHealthy(body: unknown): body is HealthPayload {
  return (
    typeof body === "object" &&
    body !== null &&
    "status" in body &&
    (body as HealthPayload).status === "ok"
  );
}

/**
 * Polls the local API's health endpoint so the shell can show a truthful
 * "local server unavailable" state instead of silently failing requests.
 *
 * A plain `res.ok` check is not sufficient in dev: without the Vite proxy
 * (vite.config.ts) a relative "/api/health" request would 200 against Vite's
 * own HTML fallback rather than the real API, and that HTML response is not
 * distinguishable from a real API 200 by status code alone. This hook
 * therefore also validates the parsed JSON body shape, and applies a
 * per-request timeout so a hung connection resolves to "disconnected"
 * instead of leaving the shell showing a stale "connected" status forever.
 */
export function useConnectionStatus(): ConnectionStatus {
  const [status, setStatus] = useState<ConnectionStatus>("checking");
  const checkInFlight = useRef(false);

  useEffect(() => {
    let cancelled = false;

    async function checkHealth() {
      if (checkInFlight.current) return; // never overlap checks
      checkInFlight.current = true;

      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

      try {
        const res = await fetch(HEALTH_ENDPOINT, { signal: controller.signal });
        if (!res.ok) {
          if (!cancelled) setStatus("disconnected");
          return;
        }
        const contentType = res.headers.get("content-type") ?? "";
        if (!contentType.includes("application/json")) {
          if (!cancelled) setStatus("disconnected");
          return;
        }
        const body: unknown = await res.json();
        if (!cancelled) setStatus(isHealthy(body) ? "connected" : "disconnected");
      } catch {
        if (!cancelled) setStatus("disconnected");
      } finally {
        clearTimeout(timeout);
        checkInFlight.current = false;
      }
    }

    checkHealth();
    const interval = setInterval(checkHealth, POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, []);

  return status;
}
