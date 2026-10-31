/// <reference types="vitest/config" />
import { fileURLToPath, URL } from "node:url";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig, type Plugin } from "vite";

const apiTarget = process.env.LIFEGRAPH_API_URL ?? "http://localhost:5000";

export default defineConfig({
  plugins: [react(), tailwindcss(), refuseVisitorEntry()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  server: {
    // Same origin in dev as in production: the auth cookie (BFF, DA-010) stays first-party.
    proxy: {
      "/api": apiTarget,
      "/health": apiTarget,
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: true,
  },
});

/** The app's origin never serves the visitor's page (DA-216); it has its own build and origin. */
function refuseVisitorEntry(): Plugin {
  return {
    name: "lifegraph-refuse-visitor-entry",
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        if (request.url?.split("?")[0] === "/visitor.html") {
          response.statusCode = 404;
          response.end();
          return;
        }

        next();
      });
    },
  };
}
