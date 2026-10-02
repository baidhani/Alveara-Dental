import { describe, it, expect } from "vitest";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";

// ALV-002-C01 R05 regression (finding F2, required by the ALV-011-C01 R01 and ALV-N001 R04 reviews): the shared
// ConcurrencyConflictBanner's TITLE used --color-warning on --color-warning-bg, 3.46:1 in the light theme (AA needs 4.5:1).
// Three completed stories hid that by overriding the title colour on their own pages. This reads the real CSS and tokens:
//   1. every text pairing in the banner meets 4.5:1 in both themes over every surface (translucent tint composited);
//   2. no stylesheet other than the banner's own re-colours the title, so the defect cannot be hidden per page again.

type RGBA = [number, number, number, number];
const root = process.cwd();
const srcDir = path.join(root, "src");
const tokensCss = readFileSync(path.join(srcDir, "styles/tokens.css"), "utf-8");
const bannerCss = readFileSync(path.join(srcDir, "components/ConcurrencyConflictBanner.css"), "utf-8");

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
  const match = pattern.exec(themeBlock(theme)) ?? pattern.exec(themeBlock("light"));
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

function usedToken(css: string, selector: string, property: string): string {
  const escaped = selector.replace(/[.[\]():]/g, "\\$&");
  const rule = new RegExp("(?:^|\\})\\s*" + escaped + "\\s*\\{([^}]*)\\}", "m").exec(css);
  if (!rule) throw new Error(`rule ${selector} not found`);
  const decl = new RegExp("(?:^|[;\\s])" + property + ":\\s*var\\(--([a-z-]+)\\)").exec(rule[1]);
  if (!decl) throw new Error(`${selector} ${property} does not use a design token`);
  return decl[1];
}

const surfaces = ["color-bg", "color-surface", "color-surface-raised"];

describe.each(["light", "dark"] as const)("conflict banner - WCAG AA contrast (%s theme)", (theme) => {
  it.each([".alv-concurrency-conflict__title", ".alv-concurrency-conflict__description"])("%s meets 4.5:1 on the banner's tint over every surface", (selector) => {
    const bgToken = usedToken(bannerCss, ".alv-concurrency-conflict", "background");
    const textToken = usedToken(bannerCss, selector, "color");
    for (const surface of surfaces) {
      const bg = over(token(theme, bgToken), token(theme, surface));
      expect(ratio(token(theme, textToken), bg), `${theme} ${selector} (--${textToken}) over ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });
});

function cssFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return entry.name === "node_modules" ? [] : cssFiles(full);
    return entry.name.endsWith(".css") ? [full] : [];
  });
}

describe("the shared conflict banner's title colour is corrected once, in the shared component", () => {
  it("no page stylesheet re-colours the banner title", () => {
    const offenders = cssFiles(srcDir)
      .filter((file) => path.basename(file) !== "ConcurrencyConflictBanner.css")
      .filter((file) => /alv-concurrency-conflict__title\s*[,{]/.test(readFileSync(file, "utf-8")))
      .map((file) => path.relative(srcDir, file).replace(/\\/g, "/"));
    expect(offenders, "a per-page override hides the shared component's contrast; fix the shared component instead").toEqual([]);
  });
});
