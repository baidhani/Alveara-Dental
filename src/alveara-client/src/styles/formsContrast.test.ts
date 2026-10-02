import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import path from "node:path";

// ALV-N010: the text/background pairings the forms UI actually uses must meet WCAG AA (4.5:1) in BOTH themes. Reads the real CSS and tokens
// (compositing translucent tints over the page surface), so a later colour change cannot quietly break it. Status colours are never used as
// TEXT colours in the forms UI - the words carry the meaning and the border/tint only reinforce it - so each pair below is body text on a tint.

type RGBA = [number, number, number, number];
const read = (rel: string) => readFileSync(path.join(process.cwd(), rel), "utf-8");
const tokensCss = read("src/styles/tokens.css");
const formsCss = read("src/pages/forms/Forms.css");
const inputsCss = read("src/components/FormFieldInputs.css");

const themeBlock = (theme: "light" | "dark") => {
  const dark = tokensCss.indexOf('[data-theme="dark"]');
  return theme === "dark" ? tokensCss.slice(dark) : tokensCss.slice(0, dark);
};
const parse = (value: string): RGBA => {
  const hex = /^#([0-9a-f]{6})$/i.exec(value.trim());
  if (hex) return [0, 2, 4].map((i) => parseInt(hex[1].slice(i, i + 2), 16)).concat(1) as RGBA;
  const m = /^rgba?\(([^)]+)\)$/i.exec(value.trim())!;
  const [r, g, b, a = "1"] = m[1].split(",").map((x) => x.trim());
  return [Number(r), Number(g), Number(b), Number(a)];
};
const token = (theme: "light" | "dark", name: string): RGBA => {
  const re = new RegExp("--" + name + ":\\s*([^;]+);");
  return parse((re.exec(themeBlock(theme)) ?? re.exec(themeBlock("light")))![1]);
};
const over = (fg: RGBA, bg: RGBA): RGBA => [0, 1, 2].map((i) => Math.round(fg[i] * fg[3] + bg[i] * (1 - fg[3]))).concat(1) as RGBA;
const lum = ([r, g, b]: RGBA) => {
  const [R, G, B] = [r, g, b].map((v) => v / 255).map((c) => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)));
  return 0.2126 * R + 0.7152 * G + 0.0722 * B;
};
const ratio = (a: RGBA, b: RGBA) => {
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

/** The var(--token) a rule uses for a property (so the test follows the CSS rather than restating it). */
function usedToken(source: string, selector: string, property: string): string {
  const esc = selector.replace(/[.[\]:()>]/g, (c) => "\\" + c);
  const rule = new RegExp("(?:^|[\\s,}])" + esc + "\\s*\\{([^}]*)\\}", "m").exec(source);
  if (!rule) throw new Error(`rule ${selector} not found`);
  const decl = new RegExp("(?:^|[\\s;])" + property + ":\\s*var\\(--([a-z-]+)").exec(rule[1]);
  if (!decl) throw new Error(`${selector} ${property} does not use a design token`);
  return decl[1];
}

const SURFACES = ["color-surface", "color-bg"];

describe.each(["light", "dark"] as const)("forms UI - WCAG AA text contrast (%s theme)", (theme) => {
  const page = (name: string) => token(theme, name);
  const onTint = (tint: string, surface: string) => over(page(tint), page(surface));

  it("every status badge keeps its words readable on its tint", () => {
    const badges: [string, string][] = [
      ["alv-form-status", "color-surface"], ["alv-form-status--draft", "color-warning-bg"],
      ["alv-form-status--signed", "color-success-bg"], ["alv-form-status--void", "color-danger-bg"],
    ];
    for (const [cls, expectedBg] of badges) {
      const bg = usedToken(formsCss, `.${cls}`, "background");
      expect(bg, cls).toBe(expectedBg);
      for (const surface of SURFACES) expect(ratio(page("color-text"), onTint(bg, surface)), `${cls} on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("the badge text colour is the body text colour (status colours are never used as text)", () => {
    expect(usedToken(formsCss, ".alv-form-status", "color")).toBe("color-text");
  });

  it("banners, the wording box and the attestation keep their text readable on their background", () => {
    for (const sel of [".alv-forms__banner", ".alv-forms__wording", ".alv-forms__attestation"]) {
      const fg = sel === ".alv-forms__attestation" ? "color-text" : usedToken(formsCss, sel, "color");
      const bg = usedToken(formsCss, sel, "background");
      expect(fg, sel).toBe("color-text");
      for (const surface of SURFACES) expect(ratio(page(fg), onTint(bg, surface)), `${sel} on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
    // the void banner only changes the border and tint; its text colour is inherited from the base banner (body text)
    for (const surface of SURFACES) expect(ratio(page("color-text"), onTint(usedToken(formsCss, ".alv-forms__banner--void", "background"), surface))).toBeGreaterThanOrEqual(4.5);
    expect(usedToken(formsCss, ".alv-forms__meta", "color")).toBe("color-text");
  });

  it("muted helper text (legal note, answer labels) is readable on both page surfaces", () => {
    expect(usedToken(formsCss, ".alv-forms__legal", "color")).toBe("color-text-muted");
    expect(usedToken(inputsCss, ".alv-form-answers dt", "color")).toBe("color-text-muted");
    for (const surface of SURFACES) expect(ratio(page("color-text-muted"), page(surface)), `muted on ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it("the answers and the hash are body text on the page surface", () => {
    expect(usedToken(inputsCss, ".alv-form-answers dd", "color")).toBe("color-text");
    expect(usedToken(formsCss, ".alv-forms__hash", "color")).toBe("color-text");
    for (const surface of SURFACES) expect(ratio(page("color-text"), page(surface))).toBeGreaterThanOrEqual(4.5);
  });

  it("the selected template in the list keeps its text readable", () => {
    expect(usedToken(formsCss, ".alv-template-list__item", "color")).toBe("color-text");
    expect(ratio(page("color-text"), page(usedToken(formsCss, ".alv-template-list__item", "background")))).toBeGreaterThanOrEqual(4.5);
  });
});
