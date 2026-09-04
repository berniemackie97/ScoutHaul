import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    // Pure helpers only. This package's components are exercised through the apps that use them.
    environment: "node",
    globals: true,
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html"],
      include: ["src/**/*.{ts,tsx}"],
    },
  },
});
