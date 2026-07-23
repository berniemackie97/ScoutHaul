import { ImageResponse } from "next/og";

// Default social-share card for every page that doesn't provide its own (product pages supply their real photo).
export const alt = "Overhaul Trading Company — Quality goods, a second life";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

export default function OpengraphImage() {
  return new ImageResponse(
    (
      <div
        style={{
          height: "100%",
          width: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          background: "#14110f",
          padding: "80px",
          color: "#f5f1ea",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "18px",
            fontSize: 28,
            letterSpacing: "0.28em",
            textTransform: "uppercase",
            color: "#c8814b",
          }}
        >
          <div style={{ width: 64, height: 2, background: "#c8814b", display: "flex" }} />
          Inspected &amp; Graded
        </div>

        <div style={{ display: "flex", flexDirection: "column" }}>
          <div style={{ fontSize: 104, fontWeight: 700, lineHeight: 1, letterSpacing: "-0.02em" }}>
            Overhaul Trading Co.
          </div>
          <div style={{ display: "flex", fontSize: 42, marginTop: 28, color: "rgba(245,241,234,0.72)" }}>
            Quality goods, a second life.
          </div>
        </div>

        <div
          style={{
            display: "flex",
            fontSize: 26,
            letterSpacing: "0.16em",
            textTransform: "uppercase",
            color: "rgba(245,241,234,0.5)",
          }}
        >
          Bradenton, Florida · overhaultrading.com
        </div>
      </div>
    ),
    { ...size },
  );
}
