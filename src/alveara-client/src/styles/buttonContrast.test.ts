import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import path from "node:path";

// Gate A review (ALV-N001 R03) regression: every Button variant must meet WCAG AA (4.5:1 for its normal-size text) in
// BOTH themes in its default, hover and keyboard-focus states. The secondary button's hover/focus text previously used
// --color-primary, which in the dark theme is a button BACKGROUND colour (about 1.8:1 on the dark surface). This reads the
// real CSS files, resolves the tokens each state actually uses (translucent backgrounds composited over the surface) and
// fails if any pairing drops below 4.5:1.

type RGBA = [number, number, number, number];
const root = process.cwd();
const tokensCss = readFileSync(path.join(root, "src/styles/tokens.css"), "utf-8");
const buttonCss = readFileSync(path.join(root, "src/components/Button.css"), "utf-8");

function themeBlock(theme: "light" | "dark"): string {
  const darkStart = tokensCss.indexOf('[data-theme="dark"]');
  return theme === "dark" ? tokensCss.slice(darkStart) : tokensCss.slice(0, darkStart);
}

function parseColor(value: string): RGBA {
  const hex = /^#([0-9a-f]{6})$/i.exec(value.trim());
  if (hex) return [0, 2, 4].map((i) => parseInt(hex[1].slice(i, i + 2), 16)).concat(1) as RGBA;
  const rgba = /^rgba?\(([^)]+)\)$/i.exec(value.trim());
  if (rgba) {
    const [r, g, b, a = "1"] = rgba[1].split(",").map((x) => x.trim());
    return [Number(r), Number(g), Number(b), Number(a)];
  }
  throw new Error(`unsupported colour ${value}`);
}

function token(theme: "light" | "dark", name: string): RGBA {
  const pattern = new RegExp("--" + name + ":\\s*([^;]+);");
  const match = pattern.exec(themeBlock(theme)) ?? pattern.exec(themeBlock("light")); // a token declared once (light block) applies to both themes
  if (!match) throw new Error(`token --${name} is not defined`);
  return parseColor(match[1]);
}

const over = (fg: RGBA, bg: RGBA): RGBA => [0, 1, 2].map((i) => Math.round(fg[i] * fg[3] + bg[i] * (1 - fg[3]))).concat(1) as RGBA;

function luminance([r, g, b]: RGBA): number {
  const [R, G, B] = [r, g, b].map((v) => v / 255).map((c) => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)));
  return 0.2126 * R + 0.7152 * G + 0.0722 * B;
}

const ratio = (a: RGBA, b: RGBA) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

/** The var(--token) a rule (selector list included) uses for a property. */
function usedToken(selector: string, property: string): string {
  const escaped = selector.split("").map((c) => (".[]():".includes(c) ? "\\" + c : c)).join("");
  const rule = new RegExp("(?:^|[,}\\s])" + escaped + "\\s*(?:,[^{]*)?\\{([^}]*)\\}").exec(buttonCss);
  if (!rule) throw new Error(`rule ${selector} not found`);
  const decl = new RegExp(property + ":\\s*var\\(--([a-z-]+)\\)").exec(rule[1]);
  if (!decl) throw new Error(`${selector} ${property} does not use a design token`);
  return decl[1];
}

describe.each(["light", "dark"] as const)("Button - WCAG AA contrast (%s theme)", (theme) => {
  const surfaces = ["color-surface", "color-bg", "color-surface-raised"];
  const surfaceColour = (name: string) => token(theme, name);

  it.each(surfaces)("secondary button hover/focus text meets 4.5:1 on %s", (surface) => {
    for (const selector of [".alv-button--secondary:hover:not(:disabled)", ".alv-button--secondary:focus-visible:not(:disabled)"]) {
      const text = token(theme, usedToken(selector, "color"));
      expect(ratio(text, surfaceColour(surface)), `${theme} ${selector} on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("secondary button default text meets 4.5:1 on its own background", () => {
    const bg = token(theme, usedToken(".alv-button--secondary", "background"));
    expect(ratio(token(theme, usedToken(".alv-button--secondary", "color")), over(bg, surfaceColour("color-bg")))).toBeGreaterThanOrEqual(4.5);
  });

  it("primary button default and hover text meet 4.5:1", () => {
    const contrast = token(theme, usedToken(".alv-button--primary", "color"));
    expect(ratio(contrast, token(theme, usedToken(".alv-button--primary", "background")))).toBeGreaterThanOrEqual(4.5);
    expect(ratio(contrast, token(theme, usedToken(".alv-button--primary:hover:not(:disabled)", "background")))).toBeGreaterThanOrEqual(4.5);
  });

  it.each(surfaces)("danger button default text meets 4.5:1 on its tinted background over %s", (surface) => {
    const bg = over(token(theme, usedToken(".alv-button--danger", "background")), surfaceColour(surface));
    expect(ratio(token(theme, usedToken(".alv-button--danger", "color")), bg), `${theme} danger default on ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it("danger button hover text meets 4.5:1 on its filled background", () => {
    const bg = token(theme, usedToken(".alv-button--danger:hover:not(:disabled)", "background"));
    const text = token(theme, usedToken(".alv-button--danger:hover:not(:disabled)", "color"));
    expect(ratio(text, bg)).toBeGreaterThanOrEqual(4.5);
  });
});
