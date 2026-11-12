import { useEffect, useState } from "react";

/** Kept as it is for compatibility (ALV-N019 class C: a persisted key is not renamed). */
export const THEME_KEY = "alveara-theme";

export type Theme = "light" | "dark";

/** The saved theme, or the system preference when nothing is saved or storage is unavailable. */
export function readStoredTheme(): Theme {
  try {
    const saved = localStorage.getItem(THEME_KEY);
    if (saved === "light" || saved === "dark") return saved;
  } catch {
    /* localStorage unavailable - fall back to the system preference */
  }
  return window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/** ALV-N020 (F-156): applies the saved theme to the document. Used at the app root, so the sign-in page (outside the shell) shows the saved theme too. */
export function applyStoredTheme(): void {
  document.documentElement.setAttribute("data-theme", readStoredTheme());
}

/** The shell's theme toggle: reads the saved theme, applies and saves every change. */
export function usePersistedTheme() {
  const [theme, setTheme] = useState<Theme>(readStoredTheme);

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", theme);
    try {
      localStorage.setItem(THEME_KEY, theme);
    } catch {
      /* non-fatal: the theme choice just won't persist */
    }
  }, [theme]);

  return { theme, setTheme };
}
