import type { Metadata, Viewport } from "next";
import { Source_Sans_3, Source_Code_Pro } from "next/font/google";
import "@overhaul/ui/globals.css";

const sans = Source_Sans_3({
  subsets: ["latin"],
  variable: "--font-sans",
  display: "swap",
});

const mono = Source_Code_Pro({
  subsets: ["latin"],
  variable: "--font-mono",
  display: "swap",
  weight: ["400", "500", "600"],
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
    statusBarStyle: "black-translucent",
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
