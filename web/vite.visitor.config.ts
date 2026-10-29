import { fileURLToPath, URL } from "node:url";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";
import { visitorBundleGuard } from "./build/visitorBundleGuard";

/**
 * The visitor's own bundle (DA-216): a second entry of this project, built to its own directory
 * and served by the Host only on the share origin, under a strict CSP. In dev it runs as
 * `npm run dev:visitor` (a build in watch mode), so the real CSP is the one tested.
 */
export default defineConfig({
  plugins: [react(), tailwindcss(), visitorBundleGuard()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  build: {
    outDir: "dist-visitor",
    emptyOutDir: true,
    // Under `default-src 'none'`: no inline script for the preload polyfill, no data: assets.
    modulePreload: { polyfill: false },
    assetsInlineLimit: 0,
    rollupOptions: { input: fileURLToPath(new URL("./visitor.html", import.meta.url)) },
  },
});
