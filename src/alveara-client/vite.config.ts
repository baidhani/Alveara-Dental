import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// Canonical dev-time API port — must match src/Alveara.Api/Properties/launchSettings.json's
// "http" profile. Production deployment serves the built client and the API from the same
// origin (see ALV-N001 R01-evidence/NEXT_STORY_IMPACT.md), so this proxy only matters in dev.
const API_DEV_PORT = 5072;

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": {
        target: `http://localhost:${API_DEV_PORT}`,
        changeOrigin: true,
      },
    },
  },
});
