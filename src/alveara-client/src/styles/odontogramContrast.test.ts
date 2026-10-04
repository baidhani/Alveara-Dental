import { describe, it, expect } from "vitest";
import { ratio, ratiosOverSurfaces, readSrc, ruleColour, token, SURFACES } from "./contrastSupport";

// STORY-006: the odontogram, in both themes, from the real stylesheet and tokens. A tooth's state is carried by the WORD under its number and by the border STYLE; the border colour only
// reinforces it. So text always uses the body text colour on a plain surface (never a status colour as text), and every state border meets the 3:1 non-text contrast over every page
// surface, so even the colour part is visible.

const css = readSrc("pages/odontogram/Odontogram.css");
const STATES = ["none", "existing", "diagnosed", "planned", "completed"] as const;

/** The colour token a state's `border:` shorthand uses, for example `3px dashed var(--color-danger)`. */
function borderToken(state: string): string {
  const rule = new RegExp("\\.alv-odonto__state--" + state + "\\s*\\{[^}]*border:\\s*[^;]*var\\(--([a-z-]+)\\)").exec(css);
  if (!rule) throw new Error(`no border colour for state ${state}`);
  return rule[1];
}

describe.each(["light", "dark"] as const)("odontogram - WCAG AA contrast (%s theme)", (theme) => {
  it.each([[".alv-odonto__tooth"], [".alv-odonto__detail"], [".alv-odonto__surface"], [".alv-odonto__chip"]])("text in %s uses the body text colour on its surface at 4.5:1 over every page surface", (selector) => {
    const text = ruleColour(css, selector, "color", theme);
    const fill = ruleColour(css, selector, "background", theme);
    for (const { surface, ratio: r } of ratiosOverSurfaces(theme, text, fill)) expect(r, `${theme} ${selector} over ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it("the hover and selected backgrounds keep the body text at 4.5:1", () => {
    const text = token(theme, "color-text");
    expect(ratio(text, token(theme, "color-surface-raised"))).toBeGreaterThanOrEqual(4.5);
  });

  it.each(STATES)("the %s border colour meets 3:1 over every page surface", (state) => {
    const border = token(theme, borderToken(state));
    for (const surface of SURFACES) expect(ratio(border, token(theme, surface)), `${theme} ${state} border over ${surface}`).toBeGreaterThanOrEqual(3);
  });

  it("the muted text (side labels) sits only on the page background and meets 4.5:1", () => {
    const text = ruleColour(css, ".alv-odonto__side", "color", theme);
    expect(ratio(text, token(theme, "color-bg"))).toBeGreaterThanOrEqual(4.5);
  });
});

describe("the odontogram never depends on colour alone", () => {
  it("gives each of the four states a different border STYLE as well as colour", () => {
    const style = (s: string) => new RegExp(`\\.alv-odonto__state--${s}\\s*\\{[^}]*border:\\s*\\d+px\\s+(\\w+)`).exec(css)?.[1];
    const styles = ["existing", "diagnosed", "planned", "completed"].map(style);
    expect(styles.every(Boolean)).toBe(true);
    expect(new Set(styles).size, "each state needs its own border style").toBe(4);
  });

  it("the outline of an empty tooth and of a surface button meets 3:1 over every page surface, in both themes", () => {
    for (const theme of ["light", "dark"] as const) {
      for (const surface of SURFACES) expect(ratio(token(theme, "color-text-muted"), token(theme, surface)), `${theme} over ${surface}`).toBeGreaterThanOrEqual(3);
    }
    expect(css).toMatch(/\.alv-odonto__state--none \{ border: 1px solid var\(--color-text-muted\)/);
    expect(css).toMatch(/\.alv-odonto__surface \{[^}]*border: 1px solid var\(--color-text-muted\)/);
  });

  it("never sets `color` to a success, warning, info or danger token", () => {
    const colours = [...css.matchAll(/(?:^|[;\s{])color:\s*var\(--color-([a-z-]+)/g)].map((m) => m[1]);
    for (const name of colours) expect(name, "a status colour as text depends on the tint behind it").not.toMatch(/^(success|warning|info|danger|error)(?!-text)/);
  });
});
