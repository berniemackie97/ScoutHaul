import { defineConfig } from "wxt";

/**
 * Where this build talks to. A development build (`pnpm dev`) points at the API and portal running
 * locally; every other build at production. Either can be overridden when building, for a staging
 * API: `WXT_API_ORIGIN=https://… pnpm build`.
 */
function origins(mode: string) {
  const development = mode === "development";
  return {
    api: process.env.WXT_API_ORIGIN ?? (development ? "http://localhost:5080" : "https://scouthaul-production.up.railway.app"),
    portal: process.env.WXT_PORTAL_ORIGIN ?? (development ? "http://localhost:3001" : "https://admin.overhaultrading.com"),
  };
}

/** The marketplaces an adapter exists for. Access to each is asked for only when it is connected. */
const marketplaceOrigins = ["https://*.ebay.com/*"];

export default defineConfig({
  srcDir: "src",
  // Manifest V3 in every browser, Firefox included, so there is one extension to reason about.
  manifestVersion: 3,
  modules: ["@wxt-dev/module-react"],
  vite: ({ mode }) => ({
    define: {
      __API_ORIGIN__: JSON.stringify(origins(mode).api),
      __PORTAL_ORIGIN__: JSON.stringify(origins(mode).portal),
    },
  }),
  manifest: ({ mode, browser }) => ({
    name: "HaulScout Sync",
    description: "Keeps HaulScout in step with your marketplace accounts, from the browser you already sell in.",
    permissions: ["storage", "alarms", "scripting"],
    host_permissions: [`${origins(mode).api}/*`],
    optional_host_permissions: marketplaceOrigins,
    action: { default_title: "HaulScout Sync" },
    ...(browser === "firefox"
      ? {
          browser_specific_settings: {
            gecko: {
              id: "sync@overhaultrading.com",
              // Declaring what data leaves the browser (below) arrived in Firefox 142; running a
              // function in the page's own world, which the adapters use, in 128.
              strict_min_version: "142.0",
              // What leaves the browser: the seller's own orders — buyer names and addresses, and money.
              data_collection_permissions: {
                required: ["personallyIdentifyingInfo", "financialAndPaymentInfo"],
              },
            },
          },
        }
      : {}),
  }),
  hooks: {
    // The portal bridge runs only on this build's portal, never on any other site.
    "entrypoints:resolved": (wxt, entrypoints) => {
      for (const entrypoint of entrypoints) {
        if (entrypoint.type === "content-script" && entrypoint.name === "portal") {
          entrypoint.options.matches = [`${origins(wxt.config.mode).portal}/*`];
        }
      }
    },
  },
});
