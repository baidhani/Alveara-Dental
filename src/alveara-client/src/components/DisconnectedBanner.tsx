import "./DisconnectedBanner.css";

/**
 * Shown when the local server/API cannot be reached. This is a visible
 * failure state, not a silently-cached offline mode — per the plan's
 * offline-semantics rule, loss of LAN/server connectivity must never be
 * hidden from the user.
 */
export function DisconnectedBanner({ variant = "app" }: { variant?: "app" | "sign-in" }) {
  // ALV-N020 (F-013): on the sign-in page "Changes cannot be saved" does not apply; the signed-in wording is unchanged until the owner decides PD-01.
  if (variant === "sign-in") {
    return (
      <div className="alv-disconnected-banner" role="alert">
        <strong>Local server unavailable.</strong> Sign-in is not possible until the server can be reached again.
      </div>
    );
  }
  return (
    <div className="alv-disconnected-banner" role="alert">
      <strong>Local server unavailable.</strong> Changes cannot be saved until the connection is
      restored. This is not an offline mode — reconnect to continue.
    </div>
  );
}
