import { defineConfig } from "vitest/config";
import path from "path";

export default defineConfig({
  // The JSX runtime Next compiles with.
  esbuild: { jsx: "automatic" },
  test: {
    environment: "node",
    globals: true,
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html"],
      include: ["src/features/**/*.{ts,tsx}"],
    },
  },
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
});
