"use client";

import { useEffect } from "react";

/**
 * The last resort: a failure in the root layout itself, which is the one place the other boundaries
 * cannot cover. It replaces the document, so it has to supply its own html and body, and it cannot
 * use anything from the layout that just failed — hence the inline styles.
 */
export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error("Application failed to start:", error);
  }, [error]);

  return (
    <html lang="en">
      <body
        style={{
          margin: 0,
          minHeight: "100vh",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontFamily: "system-ui, sans-serif",
          background: "#f8fafc",
          color: "#0f172a",
        }}
      >
        <main style={{ maxWidth: "32rem", padding: "2rem", textAlign: "center" }}>
          <h1 style={{ fontSize: "1.25rem", marginBottom: "0.5rem" }}>The console could not start</h1>
          <p style={{ color: "#475569", marginBottom: "1.5rem" }}>
            Reloading usually clears this. If it keeps happening, the API may be unreachable.
          </p>
          {error.digest ? (
            <p style={{ color: "#64748b", fontSize: "0.8rem", fontFamily: "monospace" }}>
              Reference: {error.digest}
            </p>
          ) : null}
          <button
            onClick={reset}
            style={{
              marginTop: "1rem",
              padding: "0.5rem 1rem",
              borderRadius: "0.5rem",
              border: "1px solid #cbd5e1",
              background: "#0c2236",
              color: "white",
              cursor: "pointer",
            }}
          >
            Try again
          </button>
        </main>
      </body>
    </html>
  );
}
