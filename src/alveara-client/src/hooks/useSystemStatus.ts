import { useEffect, useState } from "react";

export interface SystemStatus {
  appVersion: string;
  localServerReachable: boolean;
  database: { reachable: boolean };
  backgroundRunner: { status: "not_yet_polled" | "healthy" | "stale"; lastPollUtc: string | null };
}

export type SystemStatusFetchState =
  | { kind: "loading" }
  | { kind: "error" }
  | { kind: "loaded"; status: SystemStatus };

const POLL_INTERVAL_MS = 15_000;

/** Polls the truthful admin System Status endpoint (ALV-N002). */
export function useSystemStatus(): SystemStatusFetchState {
  const [state, setState] = useState<SystemStatusFetchState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function poll() {
      try {
        const res = await fetch("/api/systemstatus");
        if (!res.ok) throw new Error(`status ${res.status}`);
        const body = (await res.json()) as SystemStatus;
        if (!cancelled) setState({ kind: "loaded", status: body });
      } catch {
        if (!cancelled) setState({ kind: "error" });
      }
    }

    poll();
    const interval = setInterval(poll, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, []);

  return state;
}

/**
 * Whether the browser's own network adapter reports a connection. This is genuinely different
 * from "local server reachable" — a laptop with Wi-Fi off can't reach anything, including a LAN
 * server, whereas a laptop connected only to the practice LAN (no internet) has navigator.onLine
 * === true but still can't reach the public internet. Neither signal implies the other.
 *
 * Caveat this page must show honestly: navigator.onLine only reflects whether the OS believes a
 * network adapter is connected — it does not actually probe public-internet reachability, so it
 * can be wrong (e.g. connected to a LAN with no upstream internet still reports true in most
 * browsers). Treat it as a best-effort signal, not a guarantee.
 */
export function usePublicInternetSignal(): boolean {
  const [online, setOnline] = useState(navigator.onLine);

  useEffect(() => {
    const goOnline = () => setOnline(true);
    const goOffline = () => setOnline(false);
    window.addEventListener("online", goOnline);
    window.addEventListener("offline", goOffline);
    return () => {
      window.removeEventListener("online", goOnline);
      window.removeEventListener("offline", goOffline);
    };
  }, []);

  return online;
}
