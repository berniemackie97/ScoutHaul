"use client";

import { useEffect } from "react";

/**
 * The last resort: a failure in the root layout, the one place the other boundaries cannot cover. It
 * replaces the document, so it supplies its own html and body — and it cannot use the site's fonts or
 * stylesheet, because the layout that loads them is what failed.
 */
export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error("The storefront failed to start:", error);
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
          fontFamily: "Georgia, 'Times New Roman', serif",
          background: "#f6f3ee",
          color: "#1c1917",
        }}
      >
        <main style={{ maxWidth: "34rem", padding: "2rem", textAlign: "center" }}>
          <h1 style={{ fontSize: "1.5rem", marginBottom: "0.75rem" }}>The shop could not load</h1>
          <p style={{ color: "#57534e", lineHeight: 1.6 }}>
            Something went wrong on our side. Reloading usually clears it.
          </p>
          <button
            onClick={reset}
            style={{
              marginTop: "1.5rem",
              padding: "0.6rem 1.4rem",
              border: "none",
              background: "#1c1917",
              color: "#f6f3ee",
              fontFamily: "ui-monospace, monospace",
              fontSize: "0.75rem",
              letterSpacing: "0.16em",
              textTransform: "uppercase",
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
