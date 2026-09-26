"use client";

import { useEffect, useState } from "react";

/**
 * Asks a route for matches as the operator types, a beat after they pause, cancelling the question before it — so the
 * picker searches everything on the server instead of filtering whatever the page happened to load.
 */
export function useServerSearch<T>(url: string | null): { results: T[]; loading: boolean; failed: boolean } {
  const [results, setResults] = useState<T[]>([]);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (url === null) return;
    const controller = new AbortController();
    const timer = setTimeout(async () => {
      setLoading(true);
      setFailed(false);
      try {
        const response = await fetch(url, { signal: controller.signal, cache: "no-store" });
        if (!response.ok) throw new Error(String(response.status));
        setResults((await response.json()) as T[]);
      } catch {
        if (!controller.signal.aborted) {
          setFailed(true);
          setResults([]);
        }
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    }, 250);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [url]);

  return { results, loading, failed };
}
