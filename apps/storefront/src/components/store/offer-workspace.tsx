"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ArrowRight, Loader2 } from "lucide-react";
import { money, type CustomerOffer } from "@/lib/commerce";

export function OfferWorkspace() {
  const [offers, setOffers] = useState<CustomerOffer[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyOfferId, setBusyOfferId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const response = await fetch("/api/offers", { cache: "no-store" });
        if (response.status === 401) {
          window.location.assign(`/sign-in?next=${encodeURIComponent("/offers")}`);
          return;
        }
        if (!response.ok) throw new Error(await response.text());
        const data = (await response.json()) as CustomerOffer[];
        if (mounted) setOffers(data);
      } catch (err) {
        if (mounted) setError(cleanError(err));
      } finally {
        if (mounted) setLoading(false);
      }
    };

    void load();
    return () => {
      mounted = false;
    };
  }, []);

  async function addOfferToCart(offer: CustomerOffer) {
    setBusyOfferId(offer.id);
    setError(null);
    try {
      const response = await fetch(`/api/offers/${offer.id}/cart`, { method: "POST" });
      if (!response.ok) throw new Error(await response.text());
      window.dispatchEvent(new Event("haulscout:cart-changed"));
      window.location.assign("/checkout?start=1");
    } catch (err) {
      setError(cleanError(err));
      setBusyOfferId(null);
    }
  }

  if (loading) {
    return (
      <div className="border border-rule bg-surface p-8 text-sm text-slate">
        Loading offers…
      </div>
    );
  }

  return (
    <div className="space-y-5">
      {error ? <div className="border border-red-500/25 bg-red-500/10 px-4 py-3 text-sm text-red-700">{error}</div> : null}

      {offers.length === 0 ? (
        <div className="border border-dashed border-rule bg-surface p-10 text-center">
          <h2 className="font-heading text-2xl text-ink">No offers yet</h2>
          <p className="mt-2 text-sm text-slate">Make an offer from a product page and it will show here.</p>
          <Link href="/products" className="mt-5 inline-flex items-center gap-2 text-sm font-semibold text-copper">
            Browse products <ArrowRight className="h-4 w-4" aria-hidden />
          </Link>
        </div>
      ) : (
        <div className="space-y-4">
          {offers.map((offer) => (
            <article key={offer.id} className="border border-rule bg-surface p-5">
              <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
                <div>
                  <Link href={`/products/${offer.slug}`} className="font-heading text-xl font-semibold text-ink hover:text-copper">
                    {offer.listingTitle}
                  </Link>
                  <div className="mt-2 flex flex-wrap gap-2 text-sm text-slate">
                    <span>Your offer: {money(offer.offerAmount)}</span>
                    <span aria-hidden>·</span>
                    <span>List price: {money(offer.listingPriceSnapshot)}</span>
                  </div>
                </div>
                <span className="w-fit rounded-full bg-background px-3 py-1 text-sm font-semibold text-ink">
                  {offer.status}
                </span>
              </div>

              {offer.responseMessage ? (
                <p className="mt-4 border-l-2 border-copper pl-3 text-sm text-slate">{offer.responseMessage}</p>
              ) : (
                <p className="mt-4 text-sm text-slate">Waiting for an operator response.</p>
              )}

              {offer.responseAmount != null ? (
                <p className="mt-3 text-sm font-semibold text-ink">Approved/counter amount: {money(offer.responseAmount)}</p>
              ) : null}
              {offer.expiresAt ? <p className="mt-1 text-xs text-slate">Expires {date(offer.expiresAt)}</p> : null}

              {offer.canCheckout ? (
                <button
                  type="button"
                  onClick={() => addOfferToCart(offer)}
                  disabled={busyOfferId === offer.id}
                  className="mt-5 inline-flex items-center gap-2 bg-copper px-5 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-ink disabled:opacity-60"
                >
                  {busyOfferId === offer.id ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : null}
                  Checkout this offer
                </button>
              ) : null}
            </article>
          ))}
        </div>
      )}
    </div>
  );
}

function date(value: string) {
  return new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
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
