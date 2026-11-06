import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tsconfigPaths from "vite-tsconfig-paths";
import { releaseChangelog } from "./changelog.vite-plugin";

export default defineConfig({
  plugins: [react(), tsconfigPaths(), releaseChangelog()],
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test-setup.ts"],
  },
});
