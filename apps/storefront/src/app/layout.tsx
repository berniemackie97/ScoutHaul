import type { Metadata } from "next";
import { ClerkProvider } from "@clerk/nextjs";
import { Inter } from "next/font/google";
import { TRPCProvider } from "@/lib/trpc-provider";
import "@overhaul/ui/globals.css";

const inter = Inter({
  subsets: ["latin"],
  variable: "--font-inter",
});

export const metadata: Metadata = {
  title: {
    default: "Overhaul Trading Co. | Quality Secondhand Goods",
    template: "%s | Overhaul Trading Co.",
  },
  description:
    "Find quality secondhand furniture, electronics, and collectibles at fair prices. Sourced with care, priced to sell.",
  keywords: [
    "secondhand",
    "resale",
    "thrift",
    "furniture",
    "electronics",
    "collectibles",
    "Florida",
    "used goods",
    "sustainable shopping",
  ],
  openGraph: {
    type: "website",
    locale: "en_US",
    siteName: "Overhaul Trading Co.",
  },
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const content = (
    <html lang="en" className={inter.variable}>
      <body className="min-h-screen bg-background font-sans antialiased">
        <TRPCProvider>{children}</TRPCProvider>
      </body>
    </html>
  );

  if (process.env.NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY) {
    return <ClerkProvider>{content}</ClerkProvider>;
  }

  return content;
}
