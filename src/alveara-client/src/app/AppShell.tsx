import { NavLink, Outlet } from "react-router-dom";
import { useEffect, useState } from "react";
import { moduleRegistry } from "./moduleRegistry";
import { useConnectionStatus } from "../hooks/useConnectionStatus";
import { DisconnectedBanner } from "../components/DisconnectedBanner";
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
 * The application shell: responsive frame, module-registry-driven navigation,
 * and a patient-context placeholder region. No permission filtering happens
 * here yet (see moduleRegistry.ts) — that is ALV-N009's job.
 */
export function AppShell() {
  const { theme, setTheme } = usePersistedTheme();
  const connectionStatus = useConnectionStatus();

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
          {moduleRegistry.map((mod) => (
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
        {connectionStatus === "disconnected" && <DisconnectedBanner />}

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
