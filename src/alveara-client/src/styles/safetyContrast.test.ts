import { describe, it, expect } from "vitest";
import { ratio, ratiosOverSurfaces, readSrc, ruleColour, token, SURFACES } from "./contrastSupport";

// ALV-N011: the patient-safety screens and the board indicator, in both themes, from the real stylesheets and tokens. Severity, status and kind are carried by WORDS and borders; every
// piece of text uses the body text colour on a surface or a status tint (never a status colour as text), and muted text only on plain surfaces - so contrast cannot depend on the tint
// behind it. Links must use a link class (a bare link takes the primary colour, a button background in the dark theme).

const css = readSrc("pages/safety/Safety.css");
const flowCss = readSrc("pages/flow/FlowBoard.css");
const TINTED: [string, string][] = [
  [".alv-safety-strip--high", "background"], [".alv-safety-strip--some", "background"], [".alv-safety-strip--gaps", "background"], [".alv-safety-strip--error", "background"],
  [".alv-safety__gaps", "background"], [".alv-safety__sev--critical", "background"], [".alv-safety__sev--moderate", "background"], [".alv-safety__sev--low", "background"], [".alv-safety__sev--none", "background"],
];

describe.each(["light", "dark"] as const)("patient safety - WCAG AA contrast (%s theme)", (theme) => {
  it.each(TINTED)("text on %s meets 4.5:1 over every page surface", (selector, property) => {
    const text = token(theme, "color-text");
    const tint = ruleColour(css, selector, property, theme);
    for (const { surface, ratio: r } of ratiosOverSurfaces(theme, text, tint)) expect(r, `${theme} ${selector} over ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it("the strip, the entries and the board indicator use the body text colour on their own surface or tint", () => {
    for (const [source, selector] of [[css, ".alv-safety-strip"], [css, ".alv-safety__entry"], [flowCss, ".flow-card__safety"]] as const) {
      const fill = ruleColour(source, selector, "background", theme);
      const text = ruleColour(source, selector, "color", theme);
      for (const { surface, ratio: r } of ratiosOverSurfaces(theme, text, fill)) expect(r, `${theme} ${selector} over ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("the retry control and the acknowledgement line use the body text colour on a plain surface", () => {
    for (const selector of [".alv-safety-strip__retry", ".alv-safety__ack"]) {
      const text = ruleColour(css, selector, "color", theme);
      for (const surface of SURFACES) expect(ratio(text, token(theme, surface)), `${theme} ${selector} on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });
});

describe("no status colour is used as text in the safety screens", () => {
  it("never sets `color` to a success, warning, info or danger token", () => {
    for (const source of [css, flowCss.slice(flowCss.indexOf(".flow-card__safety"))]) {
      const colours = [...source.matchAll(/(?:^|[;\s{])color:\s*var\(--color-([a-z-]+)/g)].map((m) => m[1]);
      for (const name of colours) expect(name, "a status colour as text depends on the tint behind it").not.toMatch(/^(success|warning|info|danger|error)(?!-text)/);
    }
  });
});

describe("links in the safety components", () => {
  it.each(["pages/safety/SafetyStrip.tsx", "pages/safety/SafetyEntryRow.tsx"])("every link in %s is styled by a link class", (file) => {
    const src = readSrc(file);
    const links = [...src.matchAll(/<SafeLink[^>]*>/g)].map((m) => m[0]);
    expect(links.length).toBeGreaterThan(0);
    for (const link of links) expect(link, "a bare link takes the primary colour, unreadable in the dark theme").toMatch(/alv-workspace__link|alv-button|linkClassName/);
  });
});
