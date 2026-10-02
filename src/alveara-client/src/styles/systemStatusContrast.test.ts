import { describe, it, expect } from "vitest";
import { ratio, readSrc, ruleColour, ratiosOverSurfaces, token } from "./contrastSupport";

// ALV-N002 R09 regression (required by the ALV-002-C01 R05 review before Gate B): the System Status page's stale-data banner
// painted --color-warning text on --color-warning-bg, 3.47:1 in the light theme (WCAG AA needs 4.5:1). This reads the real
// stylesheet and tokens and fails if any text pairing on the page drops below 4.5:1, or a status dot below 3:1 (non-text), in either theme.

const css = readSrc("pages/SystemStatusPage.css");

describe.each(["light", "dark"] as const)("System Status page - WCAG AA contrast (%s theme)", (theme) => {
  it("the stale-data banner text meets 4.5:1 on its tint over every surface", () => {
    const text = ruleColour(css, ".status-stale-banner", "color", theme);
    const tint = ruleColour(css, ".status-stale-banner", "background", theme);
    for (const { surface, ratio: r } of ratiosOverSurfaces(theme, text, tint)) {
      expect(r, `${theme} .status-stale-banner over ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it.each([".status-card h3", ".status-caveat"])("%s (muted text) meets 4.5:1 on the card surface", (selector) => {
    const text = ruleColour(css, selector, "color", theme);
    expect(ratio(text, token(theme, "color-surface")), `${theme} ${selector}`).toBeGreaterThanOrEqual(4.5);
  });

  it.each([".status-dot--success", ".status-dot--warning", ".status-dot--danger", ".status-dot--unknown"])("the status dot %s meets the 3:1 non-text minimum on the card surface", (selector) => {
    const dot = ruleColour(css, selector, "background", theme);
    expect(ratio(dot, token(theme, "color-surface")), `${theme} ${selector}`).toBeGreaterThanOrEqual(3);
  });
});
