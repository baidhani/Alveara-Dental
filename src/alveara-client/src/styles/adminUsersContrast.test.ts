import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import path from "node:path";

// Gate A review GATE-A-01 (ALV-001-C01 R07) regression: the security-administration status badges and the "View" links
// must meet WCAG AA (4.5:1) in BOTH themes. This reads the real CSS files, resolves the tokens each rule actually uses
// (including translucent status backgrounds composited over the page surface) and fails if any pairing drops below 4.5:1.

type RGBA = [number, number, number, number];
const root = process.cwd();
const tokensCss = readFileSync(path.join(root, "src/styles/tokens.css"), "utf-8");
const pageCss = readFileSync(path.join(root, "src/pages/AdminUsersPage.css"), "utf-8");

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

function over(fg: RGBA, bg: RGBA): RGBA {
  const a = fg[3];
  return [0, 1, 2].map((i) => Math.round(fg[i] * a + bg[i] * (1 - a))).concat(1) as RGBA;
}

function luminance([r, g, b]: RGBA): number {
  const [R, G, B] = [r, g, b].map((v) => v / 255).map((c) => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)));
  return 0.2126 * R + 0.7152 * G + 0.0722 * B;
}

const ratio = (a: RGBA, b: RGBA) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

/** The var(--token) a rule uses for a property, so the test follows the CSS instead of restating it. */
function usedToken(selector: string, property: string): string {
  const escaped = selector.split("").map((c) => (".[]".includes(c) ? "\\" + c : c)).join("");
  const rule = new RegExp(escaped + "\\s*\\{([^}]*)\\}").exec(pageCss);
  if (!rule) throw new Error(`rule ${selector} not found`);
  const decl = new RegExp(property + ":\\s*var\\(--([a-z-]+)\\)").exec(rule[1]);
  if (!decl) throw new Error(`${selector} ${property} does not use a design token`);
  return decl[1];
}

describe.each(["light", "dark"] as const)("security administration - WCAG AA contrast (%s theme)", (theme) => {
  const surfaces = ["color-surface", "color-bg"];

  it.each(["enabled", "disabled"])("the %s status badge text meets 4.5:1 on its real background over every page surface", (variant) => {
    const textToken = usedToken(".alv-status-badge", "color");
    const bgToken = usedToken(`.alv-status-badge--${variant}`, "background");
    for (const surface of surfaces) {
      const bg = over(token(theme, bgToken), token(theme, surface));
      expect(ratio(token(theme, textToken), bg), `${theme} ${variant} badge on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("the View link colour meets 4.5:1 on every page surface", () => {
    const linkToken = usedToken(".alv-admin-users a", "color");
    for (const surface of surfaces) {
      expect(ratio(token(theme, linkToken), token(theme, surface)), `${theme} link on ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("badge status never relies on text colour alone: the label is text and the border carries the status colour", () => {
    expect(usedToken(".alv-status-badge--enabled", "border-color")).toBe("color-success");
    expect(usedToken(".alv-status-badge--disabled", "border-color")).toBe("color-danger");
  });
});
