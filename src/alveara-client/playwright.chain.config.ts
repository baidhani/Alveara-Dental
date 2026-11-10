import { defineConfig, devices } from "@playwright/test";

/**
 * ALV-N019: config for the accumulated chain (scripts/Invoke-AccumulatedChain.ps1). The runner starts the real API on its own throw-away database
 * and then runs this config, which starts the Vite dev server (proxying /api to that API). Serial, one worker, because later stages build on earlier ones.
 * CHAIN_OUT is the run's evidence folder; the JSON report there is what scripts/chain/Build-ChainEvidence.mjs turns into the evidence file.
 */
const out = process.env.CHAIN_OUT ?? "chain-output";

export default defineConfig({
  testDir: "./chain",
  testMatch: "stage-*.spec.ts",
  fullyParallel: false,
  workers: 1,
  reporter: [["list"], ["json", { outputFile: `${out}/playwright-report.json` }]],
  outputDir: `${out}/playwright-artifacts`,
  use: { baseURL: "http://localhost:5173", trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "npm run dev -- --port 5173",
    url: "http://localhost:5173",
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
