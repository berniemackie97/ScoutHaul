import type { NextConfig } from "next";

// The .NET API host that serves inventory photos (kept in sync with NEXT_PUBLIC_API_BASE_URL).
const apiHost = (() => {
  try {
    return new URL(
      process.env.NEXT_PUBLIC_API_BASE_URL ?? "https://scouthaul-production.up.railway.app",
    ).hostname;
  } catch {
    return "scouthaul-production.up.railway.app";
  }
})();

const nextConfig: NextConfig = {
  transpilePackages: ["@overhaul/ui", "@overhaul/api-client"],
  experimental: {
    serverActions: {
      bodySizeLimit: "10mb",
    },
  },
  images: {
    remotePatterns: [{ protocol: "https", hostname: apiHost }],
  },
  // The "Sales & finance" workspace folded into Orders; finance is now reporting (Reports).
  // Keep old bookmarks/links working. Order matters — specific paths before the catch-all.
  async redirects() {
    return [
      { source: "/finance/new", destination: "/orders/new", permanent: true },
      { source: "/finance/bundled", destination: "/orders/bundled", permanent: true },
      { source: "/finance/:id", destination: "/orders/sales/:id", permanent: true },
      { source: "/finance", destination: "/analytics", permanent: true },
    ];
  },
};

export default nextConfig;
