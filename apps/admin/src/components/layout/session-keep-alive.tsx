"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { createSupabaseBrowserClient } from "@/lib/supabase/client";

/**
 * Keeps the Supabase session fresh when the app returns to the foreground.
 *
 * In a normal browser tab the client auto-refreshes the access token on a timer, so server components always read a
 * valid token. An installed iOS PWA gets suspended, that timer stops, and when it is reopened the token has expired —
 * the middleware still sees a user (getUser revalidates) but server-side API calls read the stale cookie and come back
 * empty, so pages render with 0s. On every foreground, if the token is at/near expiry we refresh it (which rewrites
 * the auth cookies) and re-run the server components so their data loads.
 */
export function SessionKeepAlive() {
  const router = useRouter();

  useEffect(() => {
    const supabase = createSupabaseBrowserClient();

    async function ensureFresh() {
      try {
        const {
          data: { session },
        } = await supabase.auth.getSession();
        if (!session) return;
        const expiresAtMs = (session.expires_at ?? 0) * 1000;
        // Refresh if the token is expired or within 2 minutes of expiring.
        if (Date.now() < expiresAtMs - 120_000) return;
        const { error } = await supabase.auth.refreshSession();
        if (!error) router.refresh();
      } catch {
        // Never let a refresh attempt take the app down — the next foreground retries.
      }
    }

    const onVisible = () => {
      if (document.visibilityState === "visible") void ensureFresh();
    };

    void ensureFresh();
    document.addEventListener("visibilitychange", onVisible);
    return () => document.removeEventListener("visibilitychange", onVisible);
  }, [router]);

  return null;
}
