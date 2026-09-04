"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { ShoppingBag, Heart, Wrench, Package, HandCoins, Loader2, LogOut } from "lucide-react";
import { Card } from "@overhaul/ui";
import { CustomerAuthModal } from "@/components/store/customer-auth-modal";
import { createSupabaseBrowserClient } from "@/lib/supabase/client";


const accountLinks = [
  {
    name: "My Offers",
    href: "/offers",
    icon: HandCoins,
    description: "Track accepted, declined, and countered offers",
  },
  {
    name: "My Orders",
    icon: ShoppingBag,
    description: "Order history and package tracking will appear here after checkout.",
  },
  {
    name: "Wishlist",
    icon: Heart,
    description: "Saved items are not enabled yet.",
  },
  {
    name: "Repair Jobs",
    icon: Wrench,
    description: "Repair tracking will connect to customer accounts in a later slice.",
  },
  {
    name: "Consignments",
    icon: Package,
    description: "Consignment tracking will connect to customer accounts in a later slice.",
  },
];

export default function AccountPage() {
  const [loading, setLoading] = useState(true);
  const [email, setEmail] = useState<string | null>(null);
  const [authOpen, setAuthOpen] = useState(false);

  useEffect(() => {
    let mounted = true;
    try {
      const supabase = createSupabaseBrowserClient();
      void supabase.auth.getUser().then(({ data }) => {
        if (!mounted) return;
        setEmail(data.user?.email ?? null);
      }).catch(() => {
        if (mounted) setEmail(null);
      }).finally(() => {
        if (mounted) setLoading(false);
      });
    } catch {
      setEmail(null);
      setLoading(false);
    }
    return () => {
      mounted = false;
    };
  }, []);

  async function signOut() {
    try {
      const supabase = createSupabaseBrowserClient();
      await supabase.auth.signOut();
    } catch {
      // If auth is misconfigured, fail closed locally and let the shopper continue browsing.
    }
    setEmail(null);
  }

  return (
    <div className="container mx-auto px-4 py-8 max-w-3xl">
      <p className="font-mono text-xs uppercase tracking-[0.25em] text-copper">Customer account</p>
      <h1 className="mt-3 font-heading text-4xl font-semibold text-ink">My Account</h1>

      {loading ? (
        <div className="mt-8 flex items-center gap-2 border border-rule bg-surface p-6 text-sm text-slate">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
          Checking account…
        </div>
      ) : email ? (
        <>
          <div className="mt-6 flex flex-col gap-3 border border-rule bg-surface p-5 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-sm text-slate">Signed in as</p>
              <p className="font-semibold text-ink">{email}</p>
            </div>
            <button
              type="button"
              onClick={signOut}
              className="inline-flex items-center justify-center gap-2 border border-rule px-4 py-2 text-sm font-semibold text-ink hover:border-copper hover:text-copper"
            >
              <LogOut className="h-4 w-4" aria-hidden />
              Sign out
            </button>
          </div>
          <div className="mt-8 grid gap-4 sm:grid-cols-2">
            {accountLinks.map((link) => (
              link.href ? (
                <Link key={link.name} href={link.href}>
                  <Card className="h-full cursor-pointer p-6 transition-shadow hover:shadow-md">
                    <link.icon className="mb-3 h-8 w-8 text-copper" />
                    <h3 className="font-semibold text-ink">{link.name}</h3>
                    <p className="mt-1 text-sm text-slate">{link.description}</p>
                  </Card>
                </Link>
              ) : (
                <Card key={link.name} className="h-full p-6 opacity-75">
                  <link.icon className="mb-3 h-8 w-8 text-copper" />
                  <div className="flex items-center justify-between gap-3">
                    <h3 className="font-semibold text-ink">{link.name}</h3>
                    <span className="rounded-full bg-background px-2 py-1 font-mono text-[10px] uppercase tracking-[0.14em] text-slate">
                      Soon
                    </span>
                  </div>
                  <p className="mt-1 text-sm text-slate">{link.description}</p>
                </Card>
              )
            ))}
          </div>
        </>
      ) : (
        <div className="mt-8 border border-rule bg-surface p-8">
          <h2 className="font-heading text-2xl font-semibold text-ink">Sign in or create an account</h2>
          <p className="mt-2 text-sm leading-6 text-slate">
            Customer accounts use a one-time email code. Use this to track offers, orders, and checkout history.
          </p>
          <button
            type="button"
            onClick={() => setAuthOpen(true)}
            className="mt-6 bg-copper px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-background"
          >
            Sign in / register
          </button>
        </div>
      )}

      <CustomerAuthModal
        open={authOpen}
        nextPath="/account"
        onClose={() => setAuthOpen(false)}
        onAuthenticated={async () => {
          const supabase = createSupabaseBrowserClient();
          const { data } = await supabase.auth.getUser();
          setEmail(data.user?.email ?? null);
        }}
      />
    </div>
  );
}
