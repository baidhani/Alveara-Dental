import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    globals: true,
    // e2e/ holds Playwright specs (real-browser tests), run via `npm run
    // test:e2e`, not vitest — they import `test`/`expect` from
    // @playwright/test, which vitest's runner cannot execute.
    exclude: ["**/node_modules/**", "e2e/**", "gate-a/**"],
  },
});
