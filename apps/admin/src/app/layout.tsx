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
    default: "Admin | Overhaul Trading Co.",
    template: "%s | Admin",
  },
  description: "Overhaul Trading Company internal admin portal",
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
