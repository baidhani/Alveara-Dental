import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import { useEffect, useRef } from "react";
import { usePersistedTheme } from "./useAppTheme";
import { useServerConnection } from "../contexts/connectionStatusContext";
import { moduleRegistry } from "./moduleRegistry";
import { useAuth } from "../contexts/AuthContext";
import { useUnsavedChangesRegistry } from "../contexts/unsavedChangesStore";
import { confirmDiscard } from "../hooks/useUnsavedChangesWarning";
import { PatientContextProvider } from "../contexts/PatientContext";
import { PatientRequirementsProvider } from "../contexts/PatientRequirementsContext";
import { PatientHeader } from "./PatientHeader";
import { NotificationBell } from "../components/NotificationBell";
import "./AppShell.css";

/**
 * The application shell: responsive frame, module-registry-driven navigation (ALV-N009: filtered
 * by the signed-in caller's real permissions), account identity/sign-out menu, session-expiry
 * notice, and the shared patient-context header. This component only ever renders behind
 * RequireAuth (see App.tsx), so `state.kind === "signed-in"` here except for the instant before
 * that guard has resolved - hooks below tolerate that instant by falling back to empty/hidden.
 */
export function AppShell() {
  const { theme, setTheme } = usePersistedTheme();
  const { state, hasPermission, logout, sessionExpiringSoon, connectionLost, identityEpoch } = useAuth();
  const server = useServerConnection();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const unsaved = useUnsavedChangesRegistry();

  // ALV-N020 (F-037): another person is now signed in in this browser (found by revalidation, e.g. a sign-in in another tab): never stay on the previous
  // person's page. The patient in context clears itself on the same signal (PatientContextProvider).
  const seenEpoch = useRef(identityEpoch);
  useEffect(() => {
    if (seenEpoch.current === identityEpoch) return;
    seenEpoch.current = identityEpoch;
    if (state.kind === "signed-in") navigate("/", { replace: true });
  }, [identityEpoch, state.kind, navigate]);

  const visibleModules = moduleRegistry.filter((mod) => (!mod.requiredPermission || hasPermission(mod.requiredPermission)) && (!mod.anyPermission || mod.anyPermission.some((p) => hasPermission(p))));

  async function handleSignOut() {
    // Signing out discards any open draft, so it asks first like every other voluntary exit.
    if (!confirmDiscard(unsaved?.anyDirty ?? false)) return;
    await logout();
    navigate("/login", { replace: true });
  }

  return (
    <PatientRequirementsProvider>
    <PatientContextProvider>
    <div className="alv-shell">
      <a href="#alv-main-content" className="skip-link">
        Skip to main content
      </a>

      <aside className="alv-shell__sidebar">
        <div className="alv-shell__brand">
          <div className="alv-shell__brand-mark">AD</div>
          <div className="alv-shell__brand-text">
            DentalKeep
            <small>Application</small>
          </div>
        </div>

        <nav className="alv-shell__nav" aria-label="Primary navigation">
          {visibleModules.map((mod) =>
            mod.activeWhen ? (
              // An entry with its own notion of "current" (e.g. Patients, which must not light up on /patients/register) is a plain
              // link whose aria-current follows that rule; NavLink would always treat a path prefix as current.
              <Link
                key={mod.id}
                to={mod.path}
                aria-current={mod.activeWhen(pathname) ? "page" : undefined}
                className={`alv-shell__nav-link${mod.activeWhen(pathname) ? " alv-shell__nav-link--active" : ""}`}
              >
                {mod.label}
              </Link>
            ) : (
              <NavLink
                key={mod.id}
                to={mod.path}
                end={mod.path === "/"}
                className={({ isActive }) => `alv-shell__nav-link${isActive ? " alv-shell__nav-link--active" : ""}`}
              >
                {mod.label}
              </NavLink>
            )
          )}
        </nav>

        <div className="alv-shell__sidebar-footer">
          <NotificationBell />
          {state.kind === "signed-in" && (
            <div className="alv-shell__account" aria-label="Account">
              <div className="alv-shell__account-identity">
                <span className="alv-shell__account-username">{state.username}</span>
                <span className="alv-shell__account-role">{state.role}</span>
              </div>
              <button type="button" className="alv-shell__sign-out" onClick={handleSignOut}>
                Sign out
              </button>
            </div>
          )}
          <button
            type="button"
            className="alv-shell__theme-toggle"
            onClick={() => setTheme(theme === "light" ? "dark" : "light")}
            aria-pressed={theme === "dark"}
          >
            {theme === "light" ? "☀ Light" : "🌙 Dark"}
          </button>
        </div>
      </aside>

      <div className="alv-shell__main-wrap">
        {/* ALV-N020 (F-155): the session check is failing with a server error while the server itself answers (the outage banner covers "cannot be
            reached" with its unchanged wording, PD-01). The screen and unsaved work stay; the check is retried. */}
        {connectionLost && server !== "disconnected" && (
          <div className="alv-shell__session-warning" role="status">
            The server is not answering normally. Your screen and unsaved work are kept, and the connection is being retried.
          </div>
        )}
        {sessionExpiringSoon && (
          <div className="alv-shell__session-warning" role="alert">
            Your session will expire soon. Save your work and sign in again to continue.
          </div>
        )}

        {/* ALV-003-C01: the shared patient identity header (the placeholder this region held until patients existed). */}
        <PatientHeader />

        <main id="alv-main-content" className="alv-shell__content" tabIndex={-1}>
          <Outlet />
        </main>
      </div>
    </div>
    </PatientContextProvider>
    </PatientRequirementsProvider>
  );
}
