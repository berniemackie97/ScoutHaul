"use client";

import { useCallback, useEffect, useState } from "react";

/** What the HaulScout Sync extension in this browser says about itself, through its portal bridge. */
export interface ExtensionStatus {
  installed: boolean;
  paired: boolean;
  agentName: string | null;
  organizationName: string | null;
  version: string | null;
}

const absent: ExtensionStatus = { installed: false, paired: false, agentName: null, organizationName: null, version: null };

type BridgeMessage = { source?: string; type?: string; answer?: Partial<ExtensionStatus> | null };

/**
 * Talks to the extension installed in this browser, if there is one. The bridge answers two
 * questions only — is this browser connected, and would it check in now — so a scan asked for here
 * starts at once rather than at the extension's next heartbeat.
 */
export function useSyncExtension() {
  const [status, setStatus] = useState<ExtensionStatus>(absent);

  useEffect(() => {
    const onMessage = (event: MessageEvent<BridgeMessage>) => {
      if (event.source !== window || event.origin !== window.location.origin || event.data?.source !== "haulscout-sync") {
        return;
      }

      if (event.data.type === "hello") {
        window.postMessage({ source: "haulscout-portal", type: "status" }, window.location.origin);
      } else if (event.data.type === "status" && event.data.answer) {
        const answer = event.data.answer;
        setStatus({
          installed: true,
          paired: answer.paired ?? false,
          agentName: answer.agentName ?? null,
          organizationName: answer.organizationName ?? null,
          version: answer.version ?? null,
        });
      }
    };

    window.addEventListener("message", onMessage);
    // The bridge may have loaded before this page subscribed; ask directly as well.
    if (document.documentElement.dataset.haulscoutSync) {
      window.postMessage({ source: "haulscout-portal", type: "status" }, window.location.origin);
    }

    return () => window.removeEventListener("message", onMessage);
  }, []);

  const wake = useCallback(() => {
    window.postMessage({ source: "haulscout-portal", type: "wake" }, window.location.origin);
  }, []);

  const recheck = useCallback(() => {
    window.postMessage({ source: "haulscout-portal", type: "status" }, window.location.origin);
  }, []);

  return { status, wake, recheck };
}
