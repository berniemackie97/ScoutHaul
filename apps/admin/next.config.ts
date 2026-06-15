import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  transpilePackages: ["@overhaul/ui", "@overhaul/api", "@overhaul/database"],
  images: {
    remotePatterns: [
      { protocol: "https", hostname: "**.r2.cloudflarestorage.com" },
      { protocol: "https", hostname: "images.unsplash.com" },
      { protocol: "https", hostname: "placehold.co" },
    ],
  },
};

export default nextConfig;
