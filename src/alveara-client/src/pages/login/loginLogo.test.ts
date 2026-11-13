import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

// ALV-N020-C02: the sign-in logo is the owner-supplied, reviewed, static SVG. These checks pin the exact file and its safety, so a different or active file fails loudly.
const FILE = resolve(__dirname, "../../assets/login/dentalkeep-logo.svg");
const REVIEWED_SHA256 = "9CECF4A6C72A91FC7C8CF225805CA7DFD756F61760EE6FE2434F1C2E76ED82C0".toLowerCase();

describe("dentalkeep-logo.svg", () => {
  const bytes = readFileSync(FILE);
  const text = bytes.toString("utf8");

  it("is byte-identical to the reviewed owner-supplied file", () => {
    expect(createHash("sha256").update(bytes).digest("hex")).toBe(REVIEWED_SHA256);
  });

  it("has the 1774x820 viewBox the layout ratio is built on", () => {
    expect(text).toMatch(/viewBox="0 0 1774 820"/);
  });

  it("is static: no script, event handler, style element, foreignObject, embedded image/font/data URI, or reference leaving the file", () => {
    expect(text).not.toMatch(/<script/i);
    expect(text).not.toMatch(/\son[a-z]+\s*=/i);
    expect(text).not.toMatch(/<style/i);
    expect(text).not.toMatch(/<foreignObject/i);
    expect(text).not.toMatch(/<image/i);
    expect(text).not.toMatch(/@font-face/i);
    expect(text).not.toMatch(/data:/i);
    expect(text).not.toMatch(/url\(\s*["']?https?:/i);
    const refs = [...text.matchAll(/(?:xlink:)?href\s*=\s*"([^"]*)"/gi)].map((m) => m[1]);
    expect(refs.length).toBeGreaterThan(0);
    for (const r of refs) expect(r, "only same-file fragment references").toMatch(/^#/);
  });
});
