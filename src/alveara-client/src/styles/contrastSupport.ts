// Shared helpers for the contrast regression tests (statusContrast, systemStatusContrast, ...): they read the REAL stylesheets and
// design tokens, resolve the token each rule actually uses (with its CSS fallback when a token is not defined), composite
// translucent backgrounds over each page surface, and compute WCAG contrast. Nothing here is a test.
import { readFileSync } from "node:fs";
import path from "node:path";

export type RGBA = [number, number, number, number];
export type Theme = "light" | "dark";

const root = process.cwd();
export const readSrc = (file: string) => readFileSync(path.join(root, "src", file), "utf-8");
const tokensCss = readSrc("styles/tokens.css");

/** The three backgrounds any content can sit on. */
export const SURFACES = ["color-bg", "color-surface", "color-surface-raised"] as const;

function themeBlock(theme: Theme): string {
  const darkStart = tokensCss.indexOf('[data-theme="dark"]');
  return theme === "dark" ? tokensCss.slice(darkStart) : tokensCss.slice(0, darkStart);
}

export function parseColor(value: string): RGBA {
  const hex = /^#([0-9a-f]{6})$/i.exec(value.trim());
  if (hex) return [0, 2, 4].map((i) => parseInt(hex[1].slice(i, i + 2), 16)).concat(1) as RGBA;
  const rgba = /^rgba?\(([^)]+)\)$/i.exec(value.trim());
  if (rgba) {
    const [r, g, b, a = "1"] = rgba[1].split(",").map((x) => x.trim());
    return [Number(r), Number(g), Number(b), Number(a)];
  }
  throw new Error(`unsupported colour ${value}`);
}

/** A design token's value in a theme (a token declared once, in the light block, applies to both). */
export function token(theme: Theme, name: string): RGBA {
  const pattern = new RegExp("--" + name + ":\\s*([^;]+);");
  const match = pattern.exec(themeBlock(theme)) ?? pattern.exec(themeBlock("light"));
  if (!match) throw new Error(`token --${name} is not defined`);
  return parseColor(match[1]);
}

export const over = (fg: RGBA, bg: RGBA): RGBA => [0, 1, 2].map((i) => Math.round(fg[i] * fg[3] + bg[i] * (1 - fg[3]))).concat(1) as RGBA;

function luminance([r, g, b]: RGBA): number {
  const [R, G, B] = [r, g, b].map((v) => v / 255).map((c) => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)));
  return 0.2126 * R + 0.7152 * G + 0.0722 * B;
}

export const ratio = (a: RGBA, b: RGBA) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

/** The colour a rule gives a property: `var(--token)`, `var(--token, fallback)` (the fallback applies when the token is not defined), or a literal. */
export function ruleColour(css: string, selector: string, property: string, theme: Theme): RGBA {
  const escaped = selector.replace(/[.[\]():]/g, "\\$&");
  const rule = new RegExp("(?:^|\\})\\s*" + escaped + "\\s*\\{([^}]*)\\}", "m").exec(css);
  if (!rule) throw new Error(`rule ${selector} not found`);
  const decl = new RegExp("(?:^|[;\\s])" + property + ":\\s*([^;]+);").exec(rule[1]);
  if (!decl) throw new Error(`${selector} has no ${property}`);
  const value = decl[1].trim();
  const withFallback = /^var\(--([a-z-]+)\s*,\s*(.+)\)$/.exec(value);
  if (withFallback) {
    try {
      return token(theme, withFallback[1]);
    } catch {
      return parseColor(withFallback[2]); // token not defined anywhere: the fallback is what the browser paints
    }
  }
  const plain = /^var\(--([a-z-]+)\)$/.exec(value);
  if (plain) return token(theme, plain[1]);
  return parseColor(value);
}

/** Contrast of `text` on `background` composited over every page surface. */
export function ratiosOverSurfaces(theme: Theme, text: RGBA, background: RGBA): { surface: string; ratio: number }[] {
  return SURFACES.map((surface) => ({ surface, ratio: ratio(text, over(background, token(theme, surface))) }));
}
