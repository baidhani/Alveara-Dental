import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useEffect, useState } from "react";
import { moduleRegistry } from "./moduleRegistry";
import { useAuth } from "../contexts/AuthContext";
import { useUnsavedChangesRegistry } from "../contexts/unsavedChangesStore";
import { confirmDiscard } from "../hooks/useUnsavedChangesWarning";
import "./AppShell.css";

const THEME_KEY = "alveara-theme";

function usePersistedTheme() {
  const [theme, setTheme] = useState<"light" | "dark">(() => {
    try {
      const saved = localStorage.getItem(THEME_KEY);
      if (saved === "light" || saved === "dark") return saved;
    } catch {
      /* localStorage unavailable — fall back to system preference */
    }
    return window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  });

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", theme);
    try {
      localStorage.setItem(THEME_KEY, theme);
    } catch {
      /* non-fatal: theme choice just won't persist */
    }
  }, [theme]);

  return { theme, setTheme };
}

/**
 * The application shell: responsive frame, module-registry-driven navigation (ALV-N009: filtered
 * by the signed-in caller's real permissions), account identity/sign-out menu, session-expiry
 * notice, and a patient-context placeholder region. This component only ever renders behind
 * RequireAuth (see App.tsx), so `state.kind === "signed-in"` here except for the instant before
 * that guard has resolved - hooks below tolerate that instant by falling back to empty/hidden.
 */
export function AppShell() {
  const { theme, setTheme } = usePersistedTheme();
  const { state, hasPermission, logout, sessionExpiringSoon } = useAuth();
  const navigate = useNavigate();
  const unsaved = useUnsavedChangesRegistry();

  const visibleModules = moduleRegistry.filter((mod) => !mod.requiredPermission || hasPermission(mod.requiredPermission));

  async function handleSignOut() {
    // Signing out discards any open draft, so it asks first like every other voluntary exit.
    if (!confirmDiscard(unsaved?.anyDirty ?? false)) return;
    await logout();
    navigate("/login", { replace: true });
  }

  return (
    <div className="alv-shell">
      <a href="#alv-main-content" className="skip-link">
        Skip to main content
      </a>

      <aside className="alv-shell__sidebar">
        <div className="alv-shell__brand">
          <div className="alv-shell__brand-mark">AD</div>
          <div className="alv-shell__brand-text">
            Alveara Dental
            <small>Application</small>
          </div>
        </div>

        <nav className="alv-shell__nav" aria-label="Primary navigation">
          {visibleModules.map((mod) => (
            <NavLink
              key={mod.id}
              to={mod.path}
              end={mod.path === "/"}
              className={({ isActive }) => `alv-shell__nav-link${isActive ? " alv-shell__nav-link--active" : ""}`}
            >
              {mod.label}
            </NavLink>
          ))}
        </nav>

        <div className="alv-shell__sidebar-footer">
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
        {sessionExpiringSoon && (
          <div className="alv-shell__session-warning" role="alert">
            Your session will expire soon. Save your work and sign in again to continue.
          </div>
        )}

        {/* Patient-context placeholder region: no patient concept exists yet
            (registration ships in ALV-003-C01). Later stories replace this
            exact region with the real patient-context header rather than
            inventing a separate patient shell. */}
        <div className="alv-shell__patient-context" aria-hidden="true">
          <span className="alv-shell__patient-context-placeholder">
            No patient selected — patient context is not yet implemented
          </span>
        </div>

        <main id="alv-main-content" className="alv-shell__content" tabIndex={-1}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
