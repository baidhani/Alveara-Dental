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
        target: `http://127.0.0.1:${API_DEV_PORT}`,   // ALV-N030 (G8): the chain host listens on 127.0.0.1 only (Node may resolve "localhost" to ::1); the dev API (Kestrel "localhost") answers on both
        changeOrigin: true,
      },
    },
  },
});
