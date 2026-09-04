"use client";

import { useEffect } from "react";
import { ErrorPanel } from "@/components/feedback/error-panel";

/**
 * Catches a failed page inside the console.
 *
 * Without a boundary in this segment a throw reached Next's root handler, which replaced the whole
 * document — sidebar, navigation and all — so a single unreachable API call left the operator with a
 * blank page and no way out but the browser's back button. Living inside (dashboard) means the shell
 * survives and only the page area is replaced.
 */
export default function DashboardError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error("Dashboard page failed:", error);
  }, [error]);

  return (
    <ErrorPanel
      title="This page could not be loaded"
      description="Something went wrong fetching this data. The rest of the console still works, so you can try again or move on."
      onRetry={reset}
      digest={error.digest}
    />
  );
}
