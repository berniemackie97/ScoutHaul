"use client";

import { useEffect } from "react";
import { ErrorPanel } from "@/components/feedback/error-panel";

/** Covers what sits outside the console — sign-in and the auth callback. */
export default function RootError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error("Page failed:", error);
  }, [error]);

  return (
    <main className="mx-auto flex min-h-screen max-w-lg items-center px-4">
      <ErrorPanel
        title="Something went wrong"
        description="This page could not be loaded. Trying again often clears it."
        onRetry={reset}
        digest={error.digest}
      />
    </main>
  );
}
