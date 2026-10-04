import { describe, it, expect } from "vitest";
import { ratio, ratiosOverSurfaces, readSrc, ruleColour, token, SURFACES } from "./contrastSupport";

// ALV-005-C01: the longitudinal record, notes, vitals, templates and signing screens. Same rule as STORY-005's clinical screens: status is carried by the WORD (in the chip) and a
// border; every piece of text uses the body text colour on a surface or a status tint, and muted text only on plain surfaces, so contrast cannot depend on the tint behind it in
// either theme. The sticky save indicator sits over page content, so its own opaque fill is what its text is measured on.

const css = readSrc("pages/clinical/Clinical.css");
const TINTED: string[] = [
  ".alv-clinical__badge--signed", ".alv-clinical__badge--item-active", ".alv-clinical__badge--item-inactive", ".alv-clinical__badge--item-discontinued", ".alv-clinical__badge--item-resolved",
  ".alv-clinical__badge--voided", ".alv-clinical__section-status--rec-reviewed", ".alv-clinical__section-status--rec-noneknown", ".alv-clinical__section-status--rec-needsreview",
  ".alv-clinical__section-status--rec-unknown", ".alv-clinical__section-status--rec-notreviewed",
];

describe.each(["light", "dark"] as const)("clinical record, notes and signing - WCAG AA contrast (%s theme)", (theme) => {
  it.each(TINTED)("text on %s meets 4.5:1 over every page surface", (selector) => {
    const text = token(theme, "color-text");
    const tint = ruleColour(css, selector, "background", theme);
    for (const { surface, ratio: r } of ratiosOverSurfaces(theme, text, tint)) expect(r, `${theme} ${selector} over ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it("the sticky save indicator is opaque and its text meets 4.5:1 on it", () => {
    const fill = ruleColour(css, ".alv-clinical__status", "background", theme);
    expect(fill[3], "a translucent indicator would be measured on whatever scrolls under it").toBe(1);
    expect(ratio(token(theme, "color-text"), fill), `${theme} status`).toBeGreaterThanOrEqual(4.5);
  });

  it("note text and the starter-text hint use the body colour or the muted colour on a plain surface", () => {
    const body = ruleColour(css, ".alv-clinical__note-body", "color", theme);
    for (const surface of SURFACES) expect(ratio(body, token(theme, surface)), `${theme} note body on ${surface}`).toBeGreaterThanOrEqual(4.5);
    const meta = ruleColour(css, ".alv-clinical__meta", "color", theme);
    for (const surface of SURFACES) expect(ratio(meta, token(theme, surface)), `${theme} meta on ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it("the template checkbox labels use the body text colour", () => {
    const check = ruleColour(css, ".alv-clinical__check", "color", theme);
    for (const surface of SURFACES) expect(ratio(check, token(theme, surface)), `${theme} check on ${surface}`).toBeGreaterThanOrEqual(4.5);
  });
});

describe("no status colour is used as text in the new clinical screens", () => {
  it("never sets `color` to a success, warning, info or danger token", () => {
    const colours = [...css.matchAll(/(?:^|[;\s{])color:\s*var\(--color-([a-z-]+)/g)].map((m) => m[1]);
    for (const name of colours) expect(name, "a status colour as text depends on the tint behind it").not.toMatch(/^(success|warning|info|danger|error)(?!-text)/);
  });
});

describe("links in the new clinical components", () => {
  it.each(["pages/clinical/notes/TemplatesPage.tsx", "pages/clinical/PatientClinicalPanel.tsx"])("every link in %s is styled by the workspace link class or as a button", (file) => {
    const links = [...readSrc(file).matchAll(/<SafeLink[^>]*>/g)].map((m) => m[0]);
    expect(links.length).toBeGreaterThan(0);
    for (const link of links) expect(link, "a bare link takes the primary colour, unreadable in the dark theme").toMatch(/alv-workspace__link|alv-button/);
  });
});
