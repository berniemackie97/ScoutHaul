"use client";

import { useEffect } from "react";
import { ShopMessage } from "@/components/feedback/shop-message";

/**
 * A failed shop page, with the header and footer still around it.
 *
 * It lives inside (shop) rather than at the root on purpose: the layout there draws the chrome, so a
 * boundary beside it replaces only the page. At the root a shopper would lose the navigation too.
 */
export default function ShopError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error("Shop page failed:", error);
  }, [error]);

  return (
    <ShopMessage
      eyebrow="Something went wrong"
      title="We couldn't load this page"
      body="This is a problem on our side, not something you did — and it does not mean the piece has sold. Try again in a moment."
      action={
        <button
          onClick={reset}
          className="bg-ink px-5 py-2 font-mono text-[12px] uppercase tracking-[0.16em] text-paper transition hover:opacity-90"
        >
          Try again
        </button>
      }
    />
  );
}
