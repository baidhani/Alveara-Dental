import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import path from "node:path";

// ALV-N001 R04 regression (findings F1 and its siblings, required by the ALV-011-C01 R01 review): every status-coloured text
// pairing in this story's files must meet WCAG AA (4.5:1) in BOTH themes over every surface it can sit on. Before R04 the
// light success notification was 4.45:1, the light warning notification 3.47:1, the dark success notification 4.42:1 / 4.05:1
// (surface / raised surface), the dark info notification 4.39:1 (raised surface) and the shell's session-warning banner used the
// failing warning pair. This reads the real CSS and tokens, resolves the token each rule actually uses (translucent backgrounds
// composited over the surface) and fails if any pairing drops below 4.5:1.

type RGBA = [number, number, number, number];
const root = process.cwd();
const read = (file: string) => readFileSync(path.join(root, file), "utf-8");
const tokensCss = read("src/styles/tokens.css");
const notificationCss = read("src/components/Notification.css");
const shellCss = read("src/app/AppShell.css");

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
  const match = pattern.exec(themeBlock(theme)) ?? pattern.exec(themeBlock("light")); // declared once (light block) = both themes
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

/** The var(--token) a rule uses for a property. The selector must be a whole rule (not part of a longer selector). */
function usedToken(css: string, selector: string, property: string): string {
  const escaped = selector.replace(/[.[\]():]/g, "\\$&");
  const rule = new RegExp("(?:^|\\})\\s*" + escaped + "\\s*\\{([^}]*)\\}", "m").exec(css);
  if (!rule) throw new Error(`rule ${selector} not found`);
  const decl = new RegExp("(?:^|[;\\s])" + property + ":\\s*var\\(--([a-z-]+)\\)").exec(rule[1]);
  if (!decl) throw new Error(`${selector} ${property} does not use a design token`);
  return decl[1];
}

const surfaces = ["color-bg", "color-surface", "color-surface-raised"];

describe.each(["light", "dark"] as const)("status colours - WCAG AA contrast (%s theme)", (theme) => {
  it.each(["info", "success", "warning", "danger"])("the %s notification text meets 4.5:1 on its tinted background over every surface", (variant) => {
    const selector = `.alv-notification--${variant}`;
    const bgToken = usedToken(notificationCss, selector, "background");
    const textToken = usedToken(notificationCss, selector, "color");
    for (const surface of surfaces) {
      const bg = over(token(theme, bgToken), token(theme, surface));
      expect(ratio(token(theme, textToken), bg), `${theme} ${selector} (--${textToken}) over ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("the shell's session-warning banner text meets 4.5:1 on its tinted background over every surface", () => {
    const selector = ".alv-shell__session-warning";
    const bgToken = usedToken(shellCss, selector, "background");
    const textToken = usedToken(shellCss, selector, "color");
    for (const surface of surfaces) {
      const bg = over(token(theme, bgToken), token(theme, surface));
      expect(ratio(token(theme, textToken), bg), `${theme} ${selector} (--${textToken}) over ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it.each(["color-success-text", "color-warning-text"])("the text-only token --%s meets 4.5:1 on every surface and on its own tint", (name) => {
    const tint = name === "color-success-text" ? "color-success-bg" : "color-warning-bg";
    for (const surface of surfaces) {
      expect(ratio(token(theme, name), token(theme, surface)), `${theme} --${name} on ${surface}`).toBeGreaterThanOrEqual(4.5);
      expect(ratio(token(theme, name), over(token(theme, tint), token(theme, surface))), `${theme} --${name} on ${tint} over ${surface}`).toBeGreaterThanOrEqual(4.5);
    }
  });
});
