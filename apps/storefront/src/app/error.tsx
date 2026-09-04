"use client";

import { useEffect } from "react";

/** Covers what sits outside the shop chrome — sign-in, sign-up and the auth callback. */
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
    <main className="mx-auto flex min-h-screen max-w-2xl flex-col justify-center px-6 text-center">
      <p className="font-mono text-[11px] uppercase tracking-[0.24em] text-copper">Something went wrong</p>
      <h1 className="mt-4 font-serif text-3xl">We couldn&rsquo;t load this page</h1>
      <p className="mx-auto mt-4 max-w-prose text-sm leading-relaxed text-ink/70">
        This is a problem on our side. Trying again often clears it.
      </p>
      <div className="mt-8">
        <button
          onClick={reset}
          className="bg-ink px-5 py-2 font-mono text-[12px] uppercase tracking-[0.16em] text-background transition hover:opacity-90"
        >
          Try again
        </button>
      </div>
    </main>
  );
}
