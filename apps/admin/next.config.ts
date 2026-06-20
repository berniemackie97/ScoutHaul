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
  images: {
    remotePatterns: [{ protocol: "https", hostname: apiHost }],
  },
};

export default nextConfig;
