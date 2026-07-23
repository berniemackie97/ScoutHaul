import type { MetadataRoute } from "next";

/**
 * Web App Manifest — makes the operator console installable to a phone/tablet home screen and launch standalone
 * (no browser chrome). iOS reads the apple-* meta tags in the layout; Android/Chrome read this.
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "HaulScout — Operator Console",
    short_name: "HaulScout",
    description: "HaulScout operator console — inventory, listings, orders, sourcing, and finances.",
    start_url: "/",
    scope: "/",
    display: "standalone",
    orientation: "portrait-primary",
    background_color: "#0c2236",
    theme_color: "#0c2236",
    categories: ["business", "productivity"],
    icons: [
      { src: "/icons/192", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icons/512", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icons/512", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
