import type { Metadata, Viewport } from "next";
import localFont from "next/font/local";
import "@overhaul/ui/globals.css";

// Served from this app, not from Google: a build that reaches a third party's CDN fails when that CDN does.
const sans = localFont({
  src: [
    { path: "./fonts/source-sans-3.woff2", weight: "200 900", style: "normal" },
    { path: "./fonts/source-sans-3-italic.woff2", weight: "200 900", style: "italic" },
  ],
  variable: "--font-sans",
  display: "swap",
});

const mono = localFont({
  src: [{ path: "./fonts/source-code-pro.woff2", weight: "200 900", style: "normal" }],
  variable: "--font-mono",
  display: "swap",
});

export const metadata: Metadata = {
  title: {
    default: "HaulScout — Operator Console",
    template: "%s · HaulScout",
  },
  description: "HaulScout operator console — inventory, listings, sourcing, and finances.",
  applicationName: "HaulScout",
  // Installability: iOS launches standalone from these apple-* tags; the manifest covers Android/Chrome.
  appleWebApp: {
    capable: true,
    title: "HaulScout",
    // "default" keeps the web view BELOW the iOS status bar / Dynamic Island. With "black-translucent" the content
    // renders underneath it, which buried the mobile top bar (and its menu button) under the notch in standalone mode.
    statusBarStyle: "default",
  },
  formatDetection: { telephone: false },
  icons: {
    icon: "/icons/192",
    shortcut: "/icons/192",
    apple: "/icons/180",
  },
  // Next emits the modern `mobile-web-app-capable`; older iOS still keys standalone launch off the apple-prefixed
  // tag, so declare it too for full coverage.
  other: { "apple-mobile-web-app-capable": "yes" },
};

export const viewport: Viewport = {
  themeColor: "#0c2236",
  width: "device-width",
  initialScale: 1,
  // Let the app paint under the iOS notch/home-indicator so a home-screen launch feels native.
  viewportFit: "cover",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" className={`${sans.variable} ${mono.variable}`}>
      <body className="min-h-screen bg-background font-sans text-foreground antialiased">{children}</body>
    </html>
  );
}
