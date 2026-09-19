import { defineConfig } from "vitest/config";
import { WxtVitest } from "wxt/testing/vitest-plugin";

export default defineConfig({
  plugins: [WxtVitest()],
  define: {
    __API_ORIGIN__: JSON.stringify("http://api.test"),
    __PORTAL_ORIGINS__: JSON.stringify(["https://portal.test", "https://portal-app.test"]),
  },
  test: {
    environment: "node",
    mockReset: true,
    restoreMocks: true,
  },
});
