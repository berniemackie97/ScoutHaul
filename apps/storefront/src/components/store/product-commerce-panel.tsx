"use client";

import { useEffect, useRef, useState, type FormEvent } from "react";
import { CheckCircle2, Loader2 } from "lucide-react";
import { CustomerAuthModal } from "@/components/store/customer-auth-modal";
import { money, type Cart, type OfferResult } from "@/lib/commerce";
import { createSupabaseBrowserClient } from "@/lib/supabase/client";

type Props = {
  listingId: string;
  title: string;
  price: number;
  availability?: string;
  acceptsOffers?: boolean;
  minimumOfferAmount?: number | null;
  maximumOfferAmount?: number | null;
};

export function ProductCommercePanel({
  listingId,
  title,
  price,
  availability = "Available",
  acceptsOffers = true,
  minimumOfferAmount,
  maximumOfferAmount,
}: Props) {
  const addInFlight = useRef(false);
  const checkoutInFlight = useRef(false);
  const [adding, setAdding] = useState(false);
  const [checkingOut, setCheckingOut] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cartHref, setCartHref] = useState("/cart");
  const [offerOpen, setOfferOpen] = useState(false);
  const [authOpen, setAuthOpen] = useState(false);
  const [isCustomerSignedIn, setIsCustomerSignedIn] = useState<boolean | null>(null);
  const [offerSending, setOfferSending] = useState(false);
  const [offerResult, setOfferResult] = useState<string | null>(null);

  const isAvailable = availability === "Available";
  const offerFloor = minimumOfferAmount ?? 1;
  const suggestedOffer = Math.max(offerFloor, Math.floor(price * 0.85));
  const defaultOffer = maximumOfferAmount == null ? suggestedOffer : Math.min(suggestedOffer, maximumOfferAmount);

  useEffect(() => {
    let mounted = true;
    try {
      const supabase = createSupabaseBrowserClient();
      void supabase.auth.getSession().then(({ data }) => {
        if (mounted) setIsCustomerSignedIn(Boolean(data.session));
      }).catch(() => {
        if (mounted) setIsCustomerSignedIn(false);
      });
    } catch {
      setIsCustomerSignedIn(false);
    }
    return () => {
      mounted = false;
    };
  }, []);

  async function addToCart() {
    if (addInFlight.current || checkoutInFlight.current) return;
    addInFlight.current = true;
    setAdding(true);
    setError(null);
    setMessage(null);
    try {
      const cart = await addListingToCart(listingId);
      ensureListingIsInCart(cart, listingId);
      dispatchCartChanged(cart);
      setCartHref(versionedPath("/cart"));
      setMessage("Added to cart.");
    } catch (err) {
      setError(cleanError(err));
    } finally {
      setAdding(false);
      addInFlight.current = false;
    }
  }

  async function buyNow() {
    if (checkoutInFlight.current || addInFlight.current) return;
    checkoutInFlight.current = true;
    setCheckingOut(true);
    setError(null);
    try {
      const cart = await addListingToCart(listingId);
      ensureListingIsInCart(cart, listingId);
      dispatchCartChanged(cart);

      window.location.assign(`/checkout?start=1&cartv=${Date.now()}`);
    } catch (err) {
      setError(cleanError(err));
      setCheckingOut(false);
      checkoutInFlight.current = false;
    }
  }

  async function submitOffer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    setOfferSending(true);
    setOfferResult(null);
    setError(null);
    try {
      const response = await fetch("/api/offers", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          storeListingId: listingId,
          offerAmount: Number(form.get("offerAmount")),
        }),
      });
      if (response.status === 401) {
        setIsCustomerSignedIn(false);
        setAuthOpen(true);
        return;
      }
      if (!response.ok) throw new Error(await response.text());
      const result = (await response.json()) as OfferResult;
      setOfferResult(result.message || "Offer received.");
      formElement.reset();
    } catch (err) {
      setError(cleanError(err));
    } finally {
      setOfferSending(false);
    }
  }

  return (
    <div className="mt-7 space-y-4">
      {isAvailable ? (
        <div className="flex flex-col gap-3 sm:flex-row">
          <button
            type="button"
            onClick={addToCart}
            disabled={adding || checkingOut}
            className="group flex-1 bg-copper px-6 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-ink transition-transform duration-300 hover:-translate-y-0.5 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {adding ? <Spinner label="Adding" /> : "Add to cart"}
          </button>
          <button
            type="button"
            onClick={buyNow}
            disabled={adding || checkingOut}
            className="flex-1 border border-ink/25 px-6 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-ink transition-colors hover:border-copper hover:text-copper disabled:cursor-not-allowed disabled:opacity-60"
          >
            {checkingOut ? <Spinner label="Opening checkout" /> : "Buy now"}
          </button>
        </div>
      ) : (
        <div className="border border-copper/30 bg-copper/[0.06] px-4 py-3 text-sm text-slate">
          This item is currently {availability.toLowerCase()} and cannot be purchased.
        </div>
      )}

      {acceptsOffers && isAvailable ? (
        <button
          type="button"
          onClick={() => {
            if (isCustomerSignedIn) {
              setOfferOpen(value => !value);
              return;
            }
            setAuthOpen(true);
          }}
          className="w-full border border-rule px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-ink transition-colors hover:border-copper hover:text-copper"
        >
          {isCustomerSignedIn ? (offerOpen ? "Close offer form" : "Make an offer") : "Sign in / register to make an offer"}
        </button>
      ) : null}

      {message ? (
        <div className="flex items-center gap-2 border border-emerald-500/25 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-800">
          <CheckCircle2 className="h-4 w-4" aria-hidden />
          {message}{" "}
          <a href={cartHref} className="font-semibold underline underline-offset-4">
            View cart
          </a>
        </div>
      ) : null}
      {error ? <div className="border border-red-500/25 bg-red-500/10 px-4 py-3 text-sm text-red-700">{error}</div> : null}

      {offerOpen ? (
        <form onSubmit={submitOffer} className="space-y-4 border border-rule bg-surface p-4">
          <div>
            <p className="font-mono text-[10px] uppercase tracking-[0.18em] text-copper">Offer on</p>
            <p className="mt-1 font-heading text-lg text-ink">{title}</p>
            <p className="text-sm text-slate">Current price: {money(price)}</p>
            <p className="mt-2 text-xs text-slate">Customer sign-in required. No password needed.</p>
          </div>
          <div>
            <Field
              label="Offer amount"
              name="offerAmount"
              type="number"
              min={String(offerFloor)}
              max={maximumOfferAmount == null ? undefined : String(maximumOfferAmount)}
              step="0.01"
              defaultValue={String(defaultOffer)}
              required
            />
            <p className="mt-1 text-xs text-slate">
              {minimumOfferAmount != null ? `Minimum ${money(minimumOfferAmount)}. ` : null}
              {maximumOfferAmount != null ? `Maximum ${money(maximumOfferAmount)}. ` : null}
              Sign in is required so your offer response stays tied to your account.
            </p>
          </div>
          <button
            type="submit"
            disabled={offerSending}
            className="w-full bg-ink px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper hover:text-ink disabled:cursor-not-allowed disabled:opacity-60"
          >
            {offerSending ? <Spinner label="Sending" /> : "Submit offer"}
          </button>
          {offerResult ? (
            <p className="text-sm font-medium text-emerald-700">
              {offerResult}{" "}
              <a href="/offers" className="underline underline-offset-4">
                View My Offers
              </a>
            </p>
          ) : null}
        </form>
      ) : null}
      <CustomerAuthModal
        open={authOpen}
        title="Sign in to make an offer"
        body="Offers require a customer account so accepted, declined, and counter offers stay tied to you securely."
        nextPath={typeof window === "undefined" ? "/account" : window.location.pathname}
        onClose={() => setAuthOpen(false)}
        onAuthenticated={() => {
          setIsCustomerSignedIn(true);
          setOfferOpen(true);
        }}
      />
    </div>
  );
}

async function addListingToCart(listingId: string): Promise<Cart> {
  const response = await fetch("/api/cart/lines", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ storeListingId: listingId }),
    cache: "no-store",
    credentials: "same-origin",
  });
  if (!response.ok) throw new Error(await response.text());
  return (await response.json()) as Cart;
}

function ensureListingIsInCart(cart: Cart, listingId: string) {
  if (!cart.lines.some((line) => line.storeListingId === listingId && line.isAvailable)) {
    throw new Error("The item could not be added to your cart. Refresh and try again.");
  }
}

function dispatchCartChanged(cart: Cart) {
  window.dispatchEvent(new CustomEvent<Cart>("haulscout:cart-changed", { detail: cart }));
}

function versionedPath(path: string) {
  return `${path}?cartv=${Date.now()}`;
}

function Field({
  label,
  name,
  type = "text",
  required,
  autoComplete,
  min,
  max,
  step,
  defaultValue,
}: {
  label: string;
  name: string;
  type?: string;
  required?: boolean;
  autoComplete?: string;
  min?: string;
  max?: string;
  step?: string;
  defaultValue?: string;
}) {
  return (
    <label className="block">
      <span className="font-mono text-[10px] uppercase tracking-[0.16em] text-slate/70">{label}</span>
      <input
        name={name}
        type={type}
        required={required}
        autoComplete={autoComplete}
        min={min}
        max={max}
        step={step}
        defaultValue={defaultValue}
        className="mt-1 w-full border border-rule bg-background px-3 py-2 text-sm text-ink outline-none transition-colors focus:border-copper"
      />
    </label>
  );
}

function Spinner({ label }: { label: string }) {
  return (
    <span className="inline-flex items-center justify-center gap-2">
      <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
      {label}
    </span>
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
