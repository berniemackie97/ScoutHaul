import type { Metadata } from "next";
import { Source_Serif_4, Source_Sans_3, Source_Code_Pro } from "next/font/google";
import { TRPCProvider } from "@/lib/trpc-provider";
import "@overhaul/ui/globals.css";

const serif = Source_Serif_4({
  subsets: ["latin"],
  variable: "--font-serif",
  display: "swap",
  weight: ["400", "500", "600", "700"],
});

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
    default: "Admin · Overhaul Trading Company",
    template: "%s · Admin",
  },
  description: "Overhaul Trading Company operator portal",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" className={`${serif.variable} ${sans.variable} ${mono.variable}`}>
      <body className="min-h-screen bg-background font-sans text-foreground antialiased">
        <TRPCProvider>{children}</TRPCProvider>
      </body>
    </html>
  );
}
