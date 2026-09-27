import "./DisconnectedBanner.css";

/**
 * Shown when the local server/API cannot be reached. This is a visible
 * failure state, not a silently-cached offline mode — per the plan's
 * offline-semantics rule, loss of LAN/server connectivity must never be
 * hidden from the user.
 */
export function DisconnectedBanner() {
  return (
    <div className="alv-disconnected-banner" role="alert">
      <strong>Local server unavailable.</strong> Changes cannot be saved until the connection is
      restored. This is not an offline mode — reconnect to continue.
    </div>
  );
}
