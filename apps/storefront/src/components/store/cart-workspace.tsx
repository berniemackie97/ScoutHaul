"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { AlertTriangle, ArrowRight, CreditCard, Loader2, MapPin, ShieldCheck, ShoppingBag, Trash2, Truck } from "lucide-react";
import { imageUrl, money, type Cart } from "@/lib/commerce";

type Props = {
  mode: "cart" | "checkout";
};

export function CartWorkspace({ mode }: Props) {
  const [cart, setCart] = useState<Cart | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [checkingOut, setCheckingOut] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void loadCart(controller.signal);
    return () => controller.abort();
  }, []);

  const unavailableLines = useMemo(() => cart?.lines.filter((line) => !line.isAvailable) ?? [], [cart]);
  const canCheckout = Boolean(cart && cart.lines.length > 0 && unavailableLines.length === 0);
  const hasFreeShipping = cart?.estimatedShippingAmount === 0;
  const remainingForFreeShipping =
    cart?.freeShippingThreshold && !hasFreeShipping
      ? Math.max(0, cart.freeShippingThreshold - cart.subtotal)
      : 0;

  async function loadCart(signal?: AbortSignal) {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch("/api/cart", { cache: "no-store", signal });
      if (!response.ok) throw new Error(await response.text());
      setCart((await response.json()) as Cart);
    } catch (err) {
      if (err instanceof DOMException && err.name === "AbortError") return;
      setError(cleanError(err));
    } finally {
      setLoading(false);
    }
  }

  async function removeLine(storeListingId: string) {
    setBusyId(storeListingId);
    setError(null);
    try {
      const response = await fetch(`/api/cart/lines/${encodeURIComponent(storeListingId)}`, { method: "DELETE" });
      if (!response.ok) throw new Error(await response.text());
      setCart((await response.json()) as Cart);
      window.dispatchEvent(new Event("haulscout:cart-changed"));
    } catch (err) {
      setError(cleanError(err));
    } finally {
      setBusyId(null);
    }
  }

  async function checkout() {
    setCheckingOut(true);
    setError(null);
    try {
      const response = await fetch("/api/checkout", { method: "POST" });
      if (!response.ok) throw new Error(await response.text());
      const result = (await response.json()) as { checkoutUrl?: string };
      if (!result.checkoutUrl) throw new Error("Checkout did not return a payment link.");
      window.location.assign(result.checkoutUrl);
    } catch (err) {
      setError(cleanError(err));
      setCheckingOut(false);
    }
  }

  const title = mode === "checkout" ? "Review your order" : "Your cart";
  const eyebrow = mode === "checkout" ? "Secure checkout" : "Cart";
  const body =
    mode === "checkout"
      ? "Next you will enter email, shipping address, delivery method, tax, and payment on Stripe's hosted checkout."
      : "One-of-one items stay available until checkout reserves them. If a line becomes unavailable, remove it and continue.";

  return (
    <main className="mx-auto max-w-6xl px-6 py-12 md:py-16">
      <div className="flex flex-col gap-4 border-b border-rule pb-8 md:flex-row md:items-end md:justify-between">
        <div>
          <p className="font-mono text-[11px] uppercase tracking-[0.24em] text-copper">{eyebrow}</p>
          <h1 className="mt-2 font-heading text-4xl font-semibold tracking-[-0.02em] text-ink">{title}</h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-slate">{body}</p>
        </div>
        <Link
          href="/products"
          className="inline-flex items-center gap-2 border border-rule px-4 py-2.5 text-sm font-semibold text-ink transition-colors hover:border-copper hover:text-copper"
        >
          Keep shopping
          <ArrowRight className="h-4 w-4" aria-hidden />
        </Link>
      </div>

      {loading ? (
        <div className="mt-10 flex min-h-64 items-center justify-center border border-rule bg-surface">
          <Loader2 className="h-6 w-6 animate-spin text-copper" aria-hidden />
          <span className="sr-only">Loading cart</span>
        </div>
      ) : cart && cart.lines.length > 0 ? (
        <div className="mt-10 grid gap-8 lg:grid-cols-[1fr_24rem]">
          {mode === "checkout" ? (
            <section className="border border-copper/30 bg-copper/[0.06] p-5 lg:col-span-2">
              <h2 className="font-heading text-2xl font-semibold text-ink">Secure checkout handoff</h2>
              <div className="mt-4 grid gap-3 text-sm text-slate md:grid-cols-3">
                <div className="flex gap-2">
                  <CreditCard className="mt-0.5 h-4 w-4 shrink-0 text-copper" aria-hidden />
                  <span>Stripe collects payment details. Card data never touches this site.</span>
                </div>
                <div className="flex gap-2">
                  <Truck className="mt-0.5 h-4 w-4 shrink-0 text-copper" aria-hidden />
                  <span>Shipping address and delivery option are selected before payment.</span>
                </div>
                <div className="flex gap-2">
                  <MapPin className="mt-0.5 h-4 w-4 shrink-0 text-copper" aria-hidden />
                  <span>Local pickup appears when available. Tax is calculated by Stripe.</span>
                </div>
              </div>
            </section>
          ) : null}

          <section className="divide-y divide-rule border border-rule bg-background">
            {cart.lines.map((line) => (
              <article key={line.storeListingId} className="grid gap-4 p-4 sm:grid-cols-[7rem_1fr_auto] sm:p-5">
                <Link href={`/products/${line.slug}`} className="relative aspect-square overflow-hidden bg-surface ring-1 ring-rule">
                  {line.imageUrl ? (
                    <Image src={imageUrl(line.imageUrl)!} alt={line.title} fill sizes="112px" className="object-cover" />
                  ) : (
                    <span className="absolute inset-0 flex items-center justify-center font-heading text-4xl text-ink/[0.06]">OT</span>
                  )}
                </Link>

                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <Link href={`/products/${line.slug}`} className="font-heading text-xl font-medium text-ink hover:text-copper">
                      {line.title}
                    </Link>
                    {!line.isAvailable ? (
                      <span className="bg-red-500/10 px-2 py-1 font-mono text-[10px] uppercase tracking-[0.14em] text-red-700">
                        Unavailable
                      </span>
                    ) : null}
                  </div>
                  <p className="mt-2 font-mono text-sm text-slate">{money(line.unitPrice)}</p>
                  {!line.isAvailable ? (
                    <p className="mt-3 flex items-center gap-2 text-sm text-red-700">
                      <AlertTriangle className="h-4 w-4" aria-hidden />
                      This item is no longer purchasable. Remove it before checkout.
                    </p>
                  ) : null}
                </div>

                <button
                  type="button"
                  onClick={() => removeLine(line.storeListingId)}
                  disabled={busyId === line.storeListingId || checkingOut}
                  className="inline-flex h-10 items-center justify-center gap-2 border border-rule px-3 text-sm font-semibold text-slate transition-colors hover:border-red-500/40 hover:text-red-700 disabled:cursor-not-allowed disabled:opacity-60 sm:self-start"
                >
                  {busyId === line.storeListingId ? (
                    <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
                  ) : (
                    <Trash2 className="h-4 w-4" aria-hidden />
                  )}
                  Remove
                </button>
              </article>
            ))}
          </section>

          <aside className="h-fit border border-rule bg-surface p-6 lg:sticky lg:top-28">
            <h2 className="font-heading text-2xl font-semibold text-ink">Order summary</h2>
            <dl className="mt-6 space-y-3 border-b border-rule pb-5 text-sm">
              <div className="flex justify-between gap-4">
                <dt className="text-slate">Item subtotal</dt>
                <dd className="font-semibold text-ink">{money(cart.subtotal)}</dd>
              </div>
              <div className="flex justify-between gap-4">
                <dt className="text-slate">Shipping</dt>
                <dd className="text-right text-slate">{cart.shippingSummary ?? "Confirmed before payment"}</dd>
              </div>
              <div className="flex justify-between gap-4">
                <dt className="text-slate">Tax</dt>
                <dd className="text-right text-slate">Calculated at checkout</dd>
              </div>
            </dl>
            <div className="mt-5 flex justify-between gap-4 font-heading text-xl font-semibold text-ink">
              <span>{mode === "checkout" ? "Item subtotal" : "Due before tax"}</span>
              <span>{money(cart.subtotal)}</span>
            </div>

            {remainingForFreeShipping > 0 ? (
              <p className="mt-4 border border-copper/25 bg-copper/[0.06] px-3 py-2 text-sm text-slate">
                Add {money(remainingForFreeShipping)} more for free shipping.
              </p>
            ) : hasFreeShipping ? (
              <p className="mt-4 border border-emerald-500/25 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-800">
                This order has free shipping.
              </p>
            ) : null}

            {unavailableLines.length > 0 ? (
              <p className="mt-4 text-sm text-red-700">Remove unavailable lines before checkout.</p>
            ) : null}
            {error ? <p className="mt-4 border border-red-500/25 bg-red-500/10 px-3 py-2 text-sm text-red-700">{error}</p> : null}

            <button
              type="button"
              onClick={mode === "cart" ? () => window.location.assign("/checkout") : checkout}
              disabled={!canCheckout || checkingOut}
              className="mt-6 flex w-full items-center justify-center gap-2 bg-copper px-6 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-ink transition-transform hover:-translate-y-0.5 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {checkingOut ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
                  Opening secure checkout
                </>
              ) : (
                <>
                  {mode === "cart" ? "Review checkout" : "Pay securely with Stripe"}
                  <ArrowRight className="h-4 w-4" aria-hidden />
                </>
              )}
            </button>

            <ul className="mt-6 space-y-3 text-sm text-slate">
              <li className="flex gap-2">
                <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-copper" aria-hidden />
                Payment is handled by Stripe-hosted Checkout.
              </li>
              <li className="flex gap-2">
                <Truck className="mt-0.5 h-4 w-4 shrink-0 text-copper" aria-hidden />
                Shipping and local pickup options are selected during checkout.
              </li>
            </ul>
          </aside>
        </div>
      ) : (
        <div className="mt-10 flex min-h-80 flex-col items-center justify-center border border-rule bg-surface px-6 text-center">
          <ShoppingBag className="h-10 w-10 text-copper" aria-hidden />
          <h2 className="mt-4 font-heading text-2xl font-semibold text-ink">Your cart is empty.</h2>
          <p className="mt-2 max-w-md text-sm text-slate">Browse the floor and add one-of-one finds when you are ready.</p>
          <Link
            href="/products"
            className="mt-6 inline-flex items-center gap-2 bg-copper px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-ink"
          >
            Shop now
            <ArrowRight className="h-4 w-4" aria-hidden />
          </Link>
        </div>
      )}
    </main>
  );
}

function cleanError(error: unknown) {
  const raw = error instanceof Error ? error.message : "Something went wrong.";
  try {
    const parsed = JSON.parse(raw) as { title?: string; detail?: string };
    return parsed.detail || parsed.title || raw;
  } catch {
    return raw.replace(/^"|"$/g, "") || "Something went wrong.";
  }
}
