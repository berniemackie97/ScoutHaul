import { OfferWorkspace } from "@/components/store/offer-workspace";

export const metadata = { title: "My offers",
  robots: { index: false, follow: true },
};

export default function OffersPage() {
  return (
    <div className="mx-auto max-w-5xl px-4 py-12 md:px-6 md:py-16">
      <p className="font-mono text-xs uppercase tracking-[0.25em] text-copper">Account</p>
      <h1 className="mt-3 font-heading text-4xl font-semibold text-ink md:text-5xl">My offers</h1>
      <p className="mt-4 max-w-2xl text-base leading-7 text-slate">
        Track buyer offers, operator responses, counters, and accepted prices. Accepted or countered offers can be
        checked out here while the item is still available.
      </p>
      <div className="mt-8">
        <OfferWorkspace />
      </div>
    </div>
  );
}
