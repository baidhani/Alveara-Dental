import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import path from "node:path";

// N001-R01-03 regression: the dark-theme primary button previously gave
// white-text contrast of ~2.8:1 (normal) / ~2.2:1 (hover), well under the
// WCAG AA 4.5:1 threshold for normal text. This test reads the actual
// tokens.css file and fails if either theme's primary/primary-hover
// background ever regresses below 4.5:1 against its contrast colour again.

function relativeLuminance(hex: string): number {
  const [r, g, b] = [0, 2, 4]
    .map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrastRatio(hexA: string, hexB: string): number {
  const [lighter, darker] = [relativeLuminance(hexA), relativeLuminance(hexB)].sort((a, b) => b - a);
  return (lighter + 0.05) / (darker + 0.05);
}

function extractVar(css: string, blockStart: number, blockEnd: number, name: string): string {
  const block = css.slice(blockStart, blockEnd);
  const match = new RegExp(`--${name}:\\s*#([0-9a-fA-F]{6})`).exec(block);
  if (!match) throw new Error(`Could not find --${name} in the given CSS block`);
  return match[1].toLowerCase();
}

describe("design tokens — WCAG AA contrast (normal text, 4.5:1)", () => {
  const css = readFileSync(path.join(process.cwd(), "src/styles/tokens.css"), "utf-8");

  // First block (:root) is the light theme; the ':root[data-theme="dark"]' block is dark.
  const darkBlockStart = css.indexOf('[data-theme="dark"]');
  const lightBlock = [0, darkBlockStart] as const;
  const darkBlock = [darkBlockStart, css.length] as const;

  it.each([
    ["light", lightBlock] as const,
    ["dark", darkBlock] as const,
  ])("%s theme: primary button background meets 4.5:1 against its contrast colour", (_label, [start, end]) => {
    const primary = extractVar(css, start, end, "color-primary");
    const contrast = extractVar(css, 0, css.indexOf('[data-theme="dark"]'), "color-primary-contrast"); // only declared once
    expect(contrastRatio(primary, contrast)).toBeGreaterThanOrEqual(4.5);
  });

  it.each([
    ["light", lightBlock] as const,
    ["dark", darkBlock] as const,
  ])("%s theme: primary button hover background meets 4.5:1 against its contrast colour", (_label, [start, end]) => {
    const hover = extractVar(css, start, end, "color-primary-hover");
    const contrast = extractVar(css, 0, css.indexOf('[data-theme="dark"]'), "color-primary-contrast");
    expect(contrastRatio(hover, contrast)).toBeGreaterThanOrEqual(4.5);
  });
});
