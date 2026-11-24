import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { ApiError } from "../services/authApi";
import { getSetupStatus } from "../services/accountsApi";
import "../pages/LoginPage.css";

type ServerMode = "unknown" | "installation_incomplete" | "upgrade_required" | "setup" | "normal";

/** Reads the server's mode from GET /api/setup/status (brief 16.2a). Anything that is not one of the four answers leaves the app as it is. */
async function readServerMode(): Promise<ServerMode> {
  try {
    const status = await getSetupStatus();
    return status && (status as { mode?: string }).mode === "setup" ? "setup" : "normal";
  } catch (err) {
    const mode = err instanceof ApiError && err.status === 503 ? (err.body as { mode?: string }).mode : undefined;
    if (mode === "installation_incomplete" || mode === "upgrade_required") return mode;
    console.warn("Reading the server's mode failed; the app opens normally", err);   // e.g. a network error: the connection banner reports it
    return "unknown";
  }
}

/**
 * ALV-N030 (brief 16.2a): before any sign-in, the client asks the server what it is for. A server that is not installed yet, or is waiting for an upgrade,
 * shows only its message (the installer or the upgrade tool resolves it, never the app). A never-set-up server opens first-run setup. Otherwise - or when
 * the question cannot be answered - the app renders as it always has.
 */
export function StartupGate({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<ServerMode>("unknown");
  const [checkedFor, setCheckedFor] = useState<string | null>(null);
  const { pathname } = useLocation();

  useEffect(() => {
    let cancelled = false;
    readServerMode().then((m) => { if (!cancelled) { setMode(m); setCheckedFor(pathname); } });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- read once at load; a move away from setup re-asks below
  }, []);

  // ALV-N030 (16.2b): after first-run setup the server restarts in Normal mode. When the app leaves /setup while it last saw Setup mode, it asks again
  // instead of sending the person back to setup on an answer that is no longer true.
  const stale = mode === "setup" && pathname !== "/setup" && checkedFor !== pathname;
  useEffect(() => {
    if (!stale) return;
    let cancelled = false;
    readServerMode().then((m) => { if (!cancelled) { setMode(m); setCheckedFor(pathname); } });
    return () => { cancelled = true; };
  }, [stale, pathname]);

  if (mode === "installation_incomplete") {
    return <ServerStateScreen title="DentalKeep is not installed yet" text="DentalKeep is not installed yet. Run the installer." />;
  }
  if (mode === "upgrade_required") {
    return <ServerStateScreen title="An upgrade is waiting" text="An upgrade is waiting. An administrator must run the upgrade tool." />;
  }
  if (stale) return null;   // asking again; the answer decides
  if (mode === "setup" && pathname !== "/setup") return <Navigate to="/setup" replace />;
  return <>{children}</>;
}

function ServerStateScreen({ title, text }: { title: string; text: string }) {
  return (
    <main className="alv-login" aria-labelledby="alv-server-state-title">
      <h1 id="alv-server-state-title">{title}</h1>
      <p role="status">{text}</p>
    </main>
  );
}
