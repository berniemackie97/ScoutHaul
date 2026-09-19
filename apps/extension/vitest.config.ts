import { defineConfig } from "vitest/config";
import { WxtVitest } from "wxt/testing/vitest-plugin";

export default defineConfig({
  plugins: [WxtVitest()],
  define: {
    __API_ORIGIN__: JSON.stringify("http://api.test"),
    __PORTAL_ORIGIN__: JSON.stringify("http://portal.test"),
  },
  test: {
    environment: "node",
    mockReset: true,
    restoreMocks: true,
  },
});
