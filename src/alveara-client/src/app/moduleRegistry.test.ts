import { describe, it, expect } from "vitest";
import { moduleRegistry, modulesFor } from "./moduleRegistry";

// ALV-N021 A2 (F-064): the developer Component Showcase is in a development build's navigation only; a production build leaves it out (the route too: App.tsx, checked on the built bundle).
describe("the developer-only Component Showcase", () => {
  it("is in the navigation of a development build and of the test run", () => {
    expect(modulesFor(true).map((m) => m.id)).toContain("showcase");
    expect(moduleRegistry.map((m) => m.id)).toContain("showcase");   // vitest runs as development
  });

  it("is not in a production build, and nothing else changes", () => {
    const prod = modulesFor(false).map((m) => m.id);
    expect(prod).not.toContain("showcase");
    expect(prod).toEqual(modulesFor(true).map((m) => m.id).filter((id) => id !== "showcase"));
  });
});
