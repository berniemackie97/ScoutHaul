import type { Metadata } from "next";
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
