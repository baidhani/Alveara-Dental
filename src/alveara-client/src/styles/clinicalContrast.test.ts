import { describe, it, expect } from "vitest";
import { ratio, ratiosOverSurfaces, readSrc, ruleColour, token, SURFACES } from "./contrastSupport";

// STORY-005: the clinical screens' text colours, in both themes, from the real stylesheet and tokens. Status is carried by words and borders; every piece of text uses the body text colour
// on a surface or a status tint, and muted text only on plain surfaces - so contrast cannot depend on the tint behind it. Links must use the workspace link class (a bare link
// takes the primary colour, which is a button background in the dark theme: 1.77:1 - found by the real-browser walkthrough, not by jsdom).

const css = readSrc("pages/clinical/Clinical.css");
const TINTED: [string, string][] = [
  [".alv-clinical__note", "background"], [".alv-clinical__badge--draft", "background"], [".alv-clinical__badge--finalized", "background"],
  [".alv-clinical__section-status--recorded", "background"], [".alv-clinical__section-status--nonereported", "background"], [".alv-clinical__section-status--empty", "background"],
  [".alv-clinical__review-item--open", "background"],
];

describe.each(["light", "dark"] as const)("clinical screens - WCAG AA contrast (%s theme)", (theme) => {
  it.each(TINTED)("text on %s meets 4.5:1 over every page surface", (selector, property) => {
    const text = token(theme, "color-text");
    const tint = ruleColour(css, selector, property, theme);
    for (const { surface, ratio: r } of ratiosOverSurfaces(theme, text, tint)) expect(r, `${theme} ${selector} over ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it("muted metadata text meets 4.5:1 on every plain surface", () => {
    const text = ruleColour(css, ".alv-clinical__meta", "color", theme);
    for (const surface of SURFACES) expect(ratio(text, token(theme, surface)), `${theme} meta on ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it("the save indicator and the entry cards use the body text colour on their surface", () => {
    for (const [selector, bg] of [[".alv-clinical__status", "background"], [".alv-clinical__entry", "background"], [".alv-clinical__section", "background"]] as const) {
      const fill = ruleColour(css, selector, bg, theme);
      for (const { surface, ratio: r } of ratiosOverSurfaces(theme, token(theme, "color-text"), fill)) expect(r, `${theme} ${selector} over ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });
});

describe("links in the clinical module", () => {
  it.each(["pages/clinical/EncounterView.tsx", "pages/clinical/PatientClinicalPanel.tsx"])("every link in %s is styled by the workspace link class or as a button", (file) => {
    const links = [...readSrc(file).matchAll(/<SafeLink[^>]*>/g)].map((m) => m[0]);
    expect(links.length).toBeGreaterThan(0);
    for (const link of links) expect(link, "a bare link takes the primary colour, unreadable in the dark theme").toMatch(/alv-workspace__link|alv-button/);
  });
});
