import { describe, it, expect } from "vitest";
import { ratiosOverSurfaces, readSrc, ruleColour } from "./contrastSupport";

// ALV-001-C01 R08 regression (required by the ALV-002-C01 R05 review before Gate B): the login page's expired-session (warning)
// banner painted --color-warning text on a translucent tint that is NOT a design token (--color-warning-surface is undefined, so the
// browser paints the hard-coded fallback): 3.16:1 over the page background in the light theme (WCAG AA needs 4.5:1). The same
// banner family also showed the MFA-settings success message with inline styles, 4.10:1 in the light theme and 4.40:1 over the
// dark raised surface. This reads the real stylesheet and tokens (resolving fallbacks, compositing the tint over every surface)
// and fails if any banner variant drops below 4.5:1 in either theme.

const css = readSrc("pages/LoginPage.css");

describe.each(["light", "dark"] as const)("login banners - WCAG AA contrast (%s theme)", (theme) => {
  it.each([".alv-login__banner--danger", ".alv-login__banner--warning", ".alv-login__banner--success"])("%s text meets 4.5:1 on its tint over every surface", (selector) => {
    const text = ruleColour(css, selector, "color", theme);
    const tint = ruleColour(css, selector, "background", theme);
    for (const { surface, ratio } of ratiosOverSurfaces(theme, text, tint)) {
      expect(ratio, `${theme} ${selector} over ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });
});

describe("status colours live in the stylesheet, not in inline styles", () => {
  it.each(["pages/MfaSettingsPage.tsx", "pages/LoginPage.tsx", "pages/MfaChallengePage.tsx", "pages/ResetPasswordPage.tsx"])("%s has no inline colour styles", (file) => {
    expect(readSrc(file)).not.toMatch(/style=\{\{[^}]*(color|background)/);
  });
});
