import { useEffect, useState } from "react";

export type ConnectionStatus = "checking" | "connected" | "disconnected";

const HEALTH_ENDPOINT = "/api/health";
const POLL_INTERVAL_MS = 15_000;

/**
 * Polls the local API's health endpoint so the shell can show a truthful
 * "local server unavailable" state instead of silently failing requests.
 * This is a liveness check only — it never becomes an offline-editing cache.
 */
export function useConnectionStatus(): ConnectionStatus {
  const [status, setStatus] = useState<ConnectionStatus>("checking");

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();

    async function checkHealth() {
      try {
        const res = await fetch(HEALTH_ENDPOINT, { signal: controller.signal });
        if (!cancelled) setStatus(res.ok ? "connected" : "disconnected");
      } catch {
        if (!cancelled) setStatus("disconnected");
      }
    }

    checkHealth();
    const interval = setInterval(checkHealth, POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      controller.abort();
      clearInterval(interval);
    };
  }, []);

  return status;
}
