import { describe, it, expect } from "vitest";
import { ratio, ratiosOverSurfaces, readSrc, ruleColour, token, SURFACES } from "./contrastSupport";

// ALV-N004 R06 regression (required by the ALV-002-C01 R05 review before Gate B): the backup page's warning check line painted
// --color-warning text straight on the page surface: 3.08:1 in the light theme (WCAG AA needs 4.5:1), and the success result
// line (--color-success) was 4.6:1 on the page background, a hair above the line. This reads the real stylesheet and tokens
// (resolving the `var(--token, fallback)` forms, compositing translucent tints over every surface) and fails if any text pairing
// on the page drops below 4.5:1, or an alert's status border below the 3:1 non-text minimum, in either theme.

const css = readSrc("pages/BackupRecoveryPage.css");

describe.each(["light", "dark"] as const)("backup page - WCAG AA contrast (%s theme)", (theme) => {
  it.each([".alv-backup-check--fail", ".alv-backup-check--warn", ".alv-backup-result--ok", ".alv-backup-result--fail"])("%s text meets 4.5:1 on every page surface", (selector) => {
    const text = ruleColour(css, selector, "color", theme);
    for (const surface of SURFACES) {
      expect(ratio(text, token(theme, surface)), `${theme} ${selector} on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it(".alv-backup-facts dt (muted label) meets 4.5:1 on every page surface", () => {
    const text = ruleColour(css, ".alv-backup-facts dt", "color", theme);
    for (const surface of SURFACES) expect(ratio(text, token(theme, surface)), `${theme} facts label on ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it.each([".alv-backup-alert--danger", ".alv-backup-alert--warning", ".alv-backup-alert--info"])("body text in %s meets 4.5:1 on the alert's tint over every surface", (selector) => {
    const text = token(theme, "color-text"); // alerts do not recolour their text: it is the page text colour
    const tint = ruleColour(css, selector, "background", theme);
    for (const { surface, ratio: r } of ratiosOverSurfaces(theme, text, tint)) expect(r, `${theme} ${selector} over ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it.each([".alv-backup-alert--danger", ".alv-backup-alert--warning"])("the status border of %s meets the 3:1 non-text minimum on every surface", (selector) => {
    const border = ruleColour(css, selector, "border-color", theme);
    for (const surface of SURFACES) expect(ratio(border, token(theme, surface)), `${theme} ${selector} border on ${surface}`).toBeGreaterThanOrEqual(3);
  });
});
