import { useEffect, useRef, useState } from "react";

export interface SystemStatus {
  appVersion: string;
  localServerReachable: boolean;
  database: { reachable: boolean };
  backgroundRunner: { status: "not_yet_polled" | "healthy" | "stale"; lastPollUtc: string | null };
}

export type SystemStatusFetchState =
  | { kind: "loading" }
  | { kind: "error" }
  | { kind: "loaded"; status: SystemStatus }
  /** A prior poll succeeded, but the most recent one failed, timed out, or returned an
   *  unexpected shape. Per N002-R01-03, a page must never keep showing old "healthy" data
   *  forever without saying so — this state carries the last confirmed status AND says when
   *  it was last actually confirmed, so the viewer can tell current from stale. */
  | { kind: "stale"; lastGoodStatus: SystemStatus; lastConfirmedAtUtc: string };

const POLL_INTERVAL_MS = 15_000;
const REQUEST_TIMEOUT_MS = 4_000;

function isSystemStatus(body: unknown): body is SystemStatus {
  if (typeof body !== "object" || body === null) return false;
  const b = body as Record<string, unknown>;
  return (
    typeof b.appVersion === "string" &&
    typeof b.localServerReachable === "boolean" &&
    typeof b.database === "object" &&
    b.database !== null &&
    typeof (b.database as Record<string, unknown>).reachable === "boolean" &&
    typeof b.backgroundRunner === "object" &&
    b.backgroundRunner !== null &&
    typeof (b.backgroundRunner as Record<string, unknown>).status === "string"
  );
}

/**
 * Polls the truthful admin System Status endpoint (ALV-N002). Mirrors
 * useConnectionStatus's crash-safety properties (N002-R01-03): a per-request timeout, an
 * in-flight guard so overlapping/out-of-order responses can't replace a newer observation with a
 * stale one, and response-shape validation — plus an explicit "stale" state so a hung or failing
 * poll after a prior success is visibly different from a currently-healthy one.
 */
export function useSystemStatus(): SystemStatusFetchState {
  const [state, setState] = useState<SystemStatusFetchState>({ kind: "loading" });
  const lastGoodRef = useRef<{ status: SystemStatus; confirmedAtUtc: string } | null>(null);
  const inFlightRef = useRef(false);

  useEffect(() => {
    let cancelled = false;

    async function poll() {
      if (inFlightRef.current) return; // never overlap; an out-of-order late response can't clobber a newer one
      inFlightRef.current = true;

      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

      try {
        const res = await fetch("/api/systemstatus", { signal: controller.signal });
        if (!res.ok) throw new Error(`status ${res.status}`);
        const body: unknown = await res.json();
        if (!isSystemStatus(body)) throw new Error("unexpected response shape");

        const confirmedAtUtc = new Date().toISOString();
        lastGoodRef.current = { status: body, confirmedAtUtc };
        if (!cancelled) setState({ kind: "loaded", status: body });
      } catch {
        if (cancelled) return;
        if (lastGoodRef.current) {
          setState({
            kind: "stale",
            lastGoodStatus: lastGoodRef.current.status,
            lastConfirmedAtUtc: lastGoodRef.current.confirmedAtUtc,
          });
        } else {
          setState({ kind: "error" });
        }
      } finally {
        clearTimeout(timeout);
        inFlightRef.current = false;
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
