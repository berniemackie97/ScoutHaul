import type { NextConfig } from "next";

// Host the API serves product images from — kept in sync with NEXT_PUBLIC_API_BASE_URL
// so next/image will optimise real inventory photos once they're wired through.
const apiHost = (() => {
  try {
    return new URL(
      process.env.NEXT_PUBLIC_API_BASE_URL ?? "https://scouthaul-production.up.railway.app",
    ).hostname;
  } catch {
    return "scouthaul-production.up.railway.app";
  }
})();

// Baseline security headers for a public storefront. A full Content-Security-Policy
// is deliberately deferred to its own tested pass (Next's inline hydration script
// needs a nonce or 'unsafe-inline'); shipping a broken CSP on a live store is worse
// than none.
const securityHeaders = [
  { key: "Strict-Transport-Security", value: "max-age=31536000; includeSubDomains" },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "SAMEORIGIN" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), browsing-topics=()" },
  { key: "X-DNS-Prefetch-Control", value: "on" },
];

const nextConfig: NextConfig = {
  transpilePackages: ["@overhaul/ui", "@overhaul/api-client"],
  reactStrictMode: true,
  poweredByHeader: false,
  images: {
    // The API already serves each photo at the size the page wants (a small JPEG thumbnail, or `?size=full` for the
    // original) with a year-long immutable cache. Running them through Vercel's optimizer added nothing, and when the
    // account's optimization quota ran out it answered 402 and every photo not already cached went blank.
    unoptimized: true,
    remotePatterns: [
      { protocol: "https", hostname: apiHost },
      { protocol: "https", hostname: "**.r2.cloudflarestorage.com" },
      { protocol: "https", hostname: "images.unsplash.com" },
      { protocol: "https", hostname: "placehold.co" },
    ],
  },
  async headers() {
    return [{ source: "/:path*", headers: securityHeaders }];
  },
};

export default nextConfig;
