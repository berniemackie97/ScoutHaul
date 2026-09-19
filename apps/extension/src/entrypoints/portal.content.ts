import { browser } from "wxt/browser";
import { defineContentScript } from "wxt/utils/define-content-script";
import type { BridgeRequest } from "../agent/messages";

/**
 * The bridge between the HaulScout portal and this extension. It runs only on the portal (the
 * build fixes which one), and passes on just two questions: is this browser connected, and would it
 * check in now — so a scan asked for in the portal starts at once instead of at the next heartbeat.
 */
export default defineContentScript({
  // Replaced at build time with this build's portal; see wxt.config.ts.
  matches: ["https://admin.overhaultrading.com/*"],
  runAt: "document_idle",
  main() {
    const post = (message: Record<string, unknown>) =>
      window.postMessage({ source: "haulscout-sync", ...message }, window.location.origin);

    window.addEventListener("message", (event: MessageEvent) => {
      if (event.source !== window || event.origin !== window.location.origin) {
        return;
      }

      const data = event.data as { source?: unknown; type?: unknown } | null;
      if (data?.source !== "haulscout-portal" || (data.type !== "status" && data.type !== "wake")) {
        return;
      }

      void browser.runtime.sendMessage({ type: data.type } satisfies BridgeRequest).then(
        (answer: unknown) => post({ type: data.type, answer }),
        () => post({ type: data.type, answer: null }),
      );
    });

    document.documentElement.dataset.haulscoutSync = browser.runtime.getManifest().version;
    post({ type: "hello", version: browser.runtime.getManifest().version });
  },
});
