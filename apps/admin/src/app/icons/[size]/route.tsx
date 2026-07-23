import { ImageResponse } from "next/og";

/**
 * App icons for the installable PWA, generated at the sizes the manifest and iOS home screen need. Ink-navy field
 * with a copper "HS" monogram — the HaulScout mark on the shared house palette. Full-bleed so it survives the
 * maskable safe-zone crop on Android and the rounded-rect mask on iOS.
 */
const SIZES = [180, 192, 512] as const;
type Size = (typeof SIZES)[number];

export function generateStaticParams() {
  return SIZES.map((size) => ({ size: String(size) }));
}

export async function GET(_request: Request, { params }: { params: Promise<{ size: string }> }) {
  const { size } = await params;
  const dimension = Number(size);
  if (!SIZES.includes(dimension as Size)) {
    return new Response("Not found", { status: 404 });
  }

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "hsl(209, 64%, 13%)",
          color: "hsl(30, 62%, 54%)",
          fontSize: dimension * 0.42,
          fontWeight: 700,
          letterSpacing: "-0.05em",
        }}
      >
        HS
      </div>
    ),
    { width: dimension, height: dimension },
  );
}
