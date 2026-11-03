// @lovable.dev/vite-tanstack-config already includes the following — do NOT add them manually
// or the app will break with duplicate plugins:
//   - tanstackStart, viteReact, tailwindcss, tsConfigPaths, cloudflare (build-only),
//     componentTagger (dev-only), VITE_* env injection, @ path alias, React/TanStack dedupe,
//     error logger plugins, and sandbox detection (port/host/strictPort).
// You can pass additional config via defineConfig({ vite: { ... } }) if needed.
import { defineConfig } from "@lovable.dev/vite-tanstack-config";
import { readFileSync } from "node:fs";

// Redirect TanStack Start's bundled server entry to src/server.ts (our SSR error wrapper).
// @cloudflare/vite-plugin builds from this — wrangler.jsonc main alone is insufficient.
// Feature 153: names the service-worker cache (`/sw.js?v=<id>`), so each deploy gets a fresh static cache.
const buildId = process.env.VITE_APP_BUILD_ID ?? Date.now().toString(36);

export default defineConfig({
  vite: {
    define: {
      "import.meta.env.VITE_APP_BUILD_ID": JSON.stringify(buildId),
    },
    server: {
      host: "127.0.0.1",
      port: 5173,
      ...(process.env.VITE_DEV_CERT_PFX
        ? {
            https: {
              pfx: readFileSync(process.env.VITE_DEV_CERT_PFX),
              passphrase: process.env.VITE_DEV_CERT_PASSWORD,
            },
          }
        : {}),
    },
  },
  tanstackStart: {
    server: { entry: "server" },
  },
});
