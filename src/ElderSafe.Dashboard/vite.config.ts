/// <reference types="vitest/config" />
import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],

  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },

  server: {
    proxy: {
      "/api": "http://localhost:5134",
      "/hubs": {
        target: "http://localhost:5134",
        ws: true,
      },
    },
  },

  build: {
    outDir: "../ElderSafe.Api/wwwroot",
    emptyOutDir: true,
  },

  // Node rather than a browser environment: what is tested here is decision
  // logic, which should not need a DOM to exercise. A component test will
  // need jsdom and can declare it per file when that day comes.
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
});
