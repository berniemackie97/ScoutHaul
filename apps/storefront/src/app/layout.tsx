import type { Metadata } from "next";
import localFont from "next/font/local";
import { Analytics } from "@vercel/analytics/next";
import { SpeedInsights } from "@vercel/speed-insights/next";
import { STORE } from "@/lib/store";
import "@overhaul/ui/globals.css";

// Served from this app, not from Google: a build that reaches a third party's CDN fails when that CDN does, and
// the fonts are the brand. Variable files, so every weight the design uses costs one request.
const serif = localFont({
  src: [
    { path: "./fonts/source-serif-4.woff2", weight: "200 900", style: "normal" },
    { path: "./fonts/source-serif-4-italic.woff2", weight: "200 900", style: "italic" },
  ],
  variable: "--font-serif",
  display: "swap",
});

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
  metadataBase: new URL(STORE.siteUrl),
  title: {
    default: "Overhaul Trading Company — Quality goods, a second life",
    template: "%s · Overhaul Trading Company",
  },
  description:
    "Quality secondhand goods, sourced carefully, described honestly, delivered reliably. Furniture, electronics, collectibles and more. Bradenton, Florida.",
  applicationName: "Overhaul Trading Company",
  keywords: [
    "secondhand",
    "resale",
    "vintage",
    "thrift",
    "collectibles",
    "electronics",
    "furniture",
    "handbags",
    "Bradenton",
    "Florida",
    "Overhaul Trading Company",
  ],
  alternates: { canonical: "/" },
  openGraph: {
    type: "website",
    locale: "en_US",
    url: STORE.siteUrl,
    siteName: "Overhaul Trading Company",
    title: "Overhaul Trading Company — Quality goods, a second life",
    description:
      "Quality secondhand goods, sourced carefully, described honestly, delivered reliably. Inspected, graded, and priced fair. Bradenton, Florida.",
  },
  twitter: {
    card: "summary_large_image",
    title: "Overhaul Trading Company — Quality goods, a second life",
    description:
      "Inspected, graded, and priced fair. Furniture, electronics, collectibles and more. Bradenton, Florida.",
  },
  robots: {
    index: true,
    follow: true,
    googleBot: { index: true, follow: true, "max-image-preview": "large", "max-snippet": -1 },
  },
};

// Organization / storefront structured data. Only verifiable facts — real address, no invented phone or ratings —
// so rich results never carry anything we can't stand behind.
const organizationJsonLd = {
  "@context": "https://schema.org",
  "@type": "Store",
  name: "Overhaul Trading Company",
  legalName: "Overhaul Trading Company LLC",
  url: STORE.siteUrl,
  image: `${STORE.siteUrl}/opengraph-image`,
  description:
    "Quality secondhand goods, sourced carefully, described honestly, delivered reliably. Every item inspected and graded.",
  address: {
    "@type": "PostalAddress",
    streetAddress: "1675 55th Avenue Cir E, Suite 201",
    addressLocality: "Bradenton",
    addressRegion: "FL",
    postalCode: "34203",
    addressCountry: "US",
  },
  areaServed: "US",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" className={`${serif.variable} ${sans.variable} ${mono.variable}`}>
      <body className="min-h-screen bg-background font-sans text-foreground antialiased">
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: JSON.stringify(organizationJsonLd) }}
        />
        {children}
        <Analytics />
        <SpeedInsights />
      </body>
    </html>
  );
}
