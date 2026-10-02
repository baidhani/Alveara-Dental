import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import path from "node:path";

// ALV-003-C01: every text/background pairing the patient pages and the shared patient header actually use must meet WCAG AA
// (4.5:1 for this text) in BOTH themes. Like the other contrast tests this reads the real CSS and resolves the tokens each rule
// uses (compositing translucent backgrounds over the page surface), so a later colour change cannot quietly break it. It exists
// because the real-browser scan found a toast at 4.45:1 on the first page that showed one while being scanned.

type RGBA = [number, number, number, number];
const root = process.cwd();
const read = (rel: string) => readFileSync(path.join(root, rel), "utf-8");
const tokensCss = read("src/styles/tokens.css");

const css = {
  workspace: read("src/pages/PatientWorkspace.css"),
  registration: read("src/pages/PatientRegistrationPage.css"),
  duplicates: read("src/components/DuplicateComparisonPanel.css"),
  picker: read("src/components/PatientPicker.css"),
  fields: read("src/components/PatientFieldsForm.css"),
  shell: read("src/app/AppShell.css"),
  banner: read("src/components/ConcurrencyConflictBanner.css"),
};

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

/** The var(--token) a rule uses for a property, so the test follows the CSS instead of restating it. */
function usedToken(source: string, selector: string, property: string): string {
  const escaped = selector.replace(/[.[\]:()>]/g, (c) => "\\" + c);
  const rule = new RegExp("(?:^|[\\s,}])" + escaped + "\\s*(?:,[^{]*)?\\{([^}]*)\\}", "m").exec(source);
  if (!rule) throw new Error(`rule ${selector} not found`);
  const decl = new RegExp("(?:^|[\\s;])" + property + ":\\s*var\\(--([a-z-]+)").exec(rule[1]);
  if (!decl) throw new Error(`${selector} ${property} does not use a design token`);
  return decl[1];
}

const SURFACES = ["color-surface", "color-bg"];
const TINTS = ["color-danger-bg", "color-success-bg", "color-info-bg", "color-warning-bg"];

/** [label, stylesheet, selector for the text colour, background it sits on]; a null background means each page surface. */
const text: [string, keyof typeof css, string, string | null][] = [
  ["workspace note", "workspace", ".alv-workspace__note", null],
  ["workspace link", "workspace", ".alv-workspace__link", null],
  ["workspace caption", "workspace", ".alv-workspace__caption", null],
  ["workspace dirty hint", "workspace", ".alv-workspace__dirty", null],
  ["workspace tab", "workspace", ".alv-workspace__tab", null],
  ["workspace checkbox label", "workspace", ".alv-workspace__checkbox", null],
  ["workspace error banner", "workspace", ".alv-workspace__banner", "color-danger-bg"],
  ["workspace saved message", "workspace", ".alv-workspace__saved", "color-success-bg"],
  ["conflict banner title (workspace override of the shared title colour)", "workspace", ".alv-workspace__form .alv-concurrency-conflict__title", "color-warning-bg"],
  ["conflict banner description (shared)", "banner", ".alv-concurrency-conflict__description", "color-warning-bg"],
  ["registration error banner", "registration", ".alv-patient-reg__banner", "color-danger-bg"],
  ["registration success panel", "registration", ".alv-patient-reg__done", "color-success-bg"],
  ["registration dirty hint", "registration", ".alv-patient-reg__dirty", null],
  ["duplicate panel", "duplicates", ".alv-duplicates", "color-surface"],
  ["duplicate panel caption", "duplicates", ".alv-duplicates__caption", "color-surface"],
  ["duplicate panel link", "duplicates", ".alv-duplicates__link", "color-surface"],
  ["picker label", "picker", ".alv-patient-picker__label", null],
  ["picker selected text", "picker", ".alv-patient-picker__selected", null],
  ["picker status", "picker", ".alv-patient-picker__status", null],
  ["picker result", "picker", ".alv-patient-picker__result", "color-surface"],
  ["patient fields legend", "fields", ".alv-patient-fields legend", null],
  ["patient header identity", "shell", ".alv-patient-header__identity", "color-surface"],
  ["patient header empty-state text", "shell", ".alv-patient-header__none", "color-surface"],
  ["patient header link", "shell", ".alv-patient-header__link", "color-surface"],
  ["patient header button", "shell", ".alv-patient-header__button", "color-surface"],
];

describe.each(["light", "dark"] as const)("patient pages - WCAG AA text contrast (%s theme)", (theme) => {
  it.each(text)("%s meets 4.5:1", (_label, sheet, selector, background) => {
    const fg = token(theme, usedToken(css[sheet], selector, "color"));
    for (const name of background === null ? SURFACES : [background]) {
      // A tinted status background can be translucent: composite it over each real page surface.
      const backgrounds = TINTS.includes(name) ? SURFACES.map((s) => over(token(theme, name), token(theme, s))) : [token(theme, name)];
      for (const bg of backgrounds) expect(ratio(fg, bg), `${theme} ${selector} on ${name}`).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("the picker result hover/focus background keeps its text readable", () => {
    const hover = usedToken(css.picker, ".alv-patient-picker__result:hover:not(:disabled)", "background");
    const fg = token(theme, usedToken(css.picker, ".alv-patient-picker__result", "color"));
    for (const surface of SURFACES) expect(ratio(fg, over(token(theme, hover), token(theme, surface))), `${theme} picker hover on ${surface}`).toBeGreaterThanOrEqual(4.5);
  });

  it("the active workspace tab keeps readable text on its background", () => {
    const fg = token(theme, usedToken(css.workspace, ".alv-workspace__tab", "color"));
    const bg = token(theme, usedToken(css.workspace, ".alv-workspace__tab--active", "background"));
    expect(ratio(fg, bg), `${theme} active tab`).toBeGreaterThanOrEqual(4.5);
  });
});

describe("the shared conflict banner's own title colour", () => {
  it("is documented as failing AA in the light theme, which is why the workspace overrides it (so the override cannot be dropped unnoticed)", () => {
    const own = token("light", usedToken(css.banner, ".alv-concurrency-conflict__title", "color"));
    const bg = token("light", usedToken(css.banner, ".alv-concurrency-conflict", "background"));
    expect(ratio(own, bg)).toBeLessThan(4.5); // if the shared component is fixed, delete the override in PatientWorkspace.css and this test
  });
});
