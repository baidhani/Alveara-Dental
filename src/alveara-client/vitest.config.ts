import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    globals: true,
    // The default 5 s per test left too thin a margin for the slowest suites (the periodontal grid, backup recovery) when the whole suite runs in parallel straight after a long backend run: each
    // passed alone in 1 to 3 s and failed only on the wall-clock limit, in a different file each time. 15 s only widens that margin; it changes no assertion and a test that hangs still fails.
    testTimeout: 15_000,
    // e2e/ holds Playwright specs (real-browser tests), run via `npm run
    // test:e2e`, not vitest — they import `test`/`expect` from
    // @playwright/test, which vitest's runner cannot execute.
    // gate-*/ holds the quality-gate evidence tooling (Playwright specs plus, for Gate B, generated specs in gate-b/tablet-generated/): evidence
    // output, run with their own Playwright configs, never by vitest (Gate B review B-REV-01). The pattern covers every future gate directory too;
    // tests/gate-tooling-coexistence.test.mjs generates the output and proves vitest does not collect it.
    exclude: ["**/node_modules/**", "e2e/**", "gate-*/**"],
  },
});
