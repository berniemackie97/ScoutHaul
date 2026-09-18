import Link from "next/link";
import { Handshake, Mail, Phone, Tags } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { formatCurrency, formatDateTime } from "@overhaul/ui";
import { StatCard, StatRow } from "@/components/list/stat-card";
import { getApiClient } from "@/lib/api";
import { decideOffer } from "./actions";

export const dynamic = "force-dynamic";

type StorefrontOffer = components["schemas"]["StorefrontOfferDto"];

const FILTERS = ["All", "New", "Accepted", "Countered", "Declined", "Expired", "Converted"] as const;
type OfferStatusFilter = Exclude<(typeof FILTERS)[number], "All">;
type OfferCounts = components["schemas"]["StorefrontOfferCountsDto"];

/** "Countered" → "countered": the counts DTO names each status in camelCase. */
const lowerFirst = (value: OfferStatusFilter) =>
  (value.charAt(0).toLowerCase() + value.slice(1)) as keyof Omit<OfferCounts, "total">;


const date = (value: string | null | undefined) =>
  formatDateTime(value);

export default async function OffersPage({
  searchParams,
}: {
  searchParams: Promise<{ status?: string }>;
}) {
  const { status } = await searchParams;
  const active = FILTERS.includes((status ?? "All") as (typeof FILTERS)[number])
    ? ((status ?? "All") as (typeof FILTERS)[number])
    : "All";

  const api = await getApiClient();
  const [{ data }, { data: counts }] = await Promise.all([
    api.GET("/api/admin/storefront/offers", {
      params: { query: active === "All" ? {} : { status: active as OfferStatusFilter } },
    }),
    api.GET("/api/admin/storefront/offers/counts", {}),
  ]);
  const offers = (data ?? []) as StorefrontOffer[];

  // "New offers" is a count of the whole book, not of the tab being viewed. It used to be counted
  // from this page's list, so on the Declined tab it read zero however many buyers were waiting.
  const newOffers = counts?.new ?? 0;
  const totalOffered = offers.reduce((sum, offer) => sum + (offer.offerAmount ?? 0), 0);
  const averageOffer = offers.length > 0 ? totalOffered / offers.length : 0;
  const countFor = (filter: (typeof FILTERS)[number]): number | null => {
    if (!counts) return null;
    return filter === "All" ? (counts.total ?? 0) : (counts[lowerFirst(filter)] ?? 0);
  };

  return (
    <div className="space-y-6">
      <header>
        <p className="text-muted-foreground text-sm">Storefront</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Offers</h1>
        <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
          Buyer account offers. Accept, decline, or counter here; the customer sees the response in My Offers and can
          check out approved offers while the item remains available.
        </p>
      </header>

      <StatRow>
        <StatCard
          label="New offers"
          value={String(newOffers)}
          detail="Waiting for your reply — all offers, whichever tab is open"
          icon={Mail}
          href={active === "New" ? undefined : "/offers?status=New"}
          tone={newOffers > 0 ? "warning" : "default"}
        />
        <StatCard label="All offers" value={String(counts?.total ?? 0)} detail="Every status" icon={Handshake} />
        <StatCard
          label="Average offer"
          value={formatCurrency(averageOffer)}
          detail={active === "All" ? "Across the offers shown" : `Across ${active.toLowerCase()} offers shown`}
          icon={Tags}
        />
      </StatRow>

      <div className="flex flex-wrap gap-2">
        {FILTERS.map((filter) => (
          <Link
            key={filter}
            href={filter === "All" ? "/offers" : `/offers?status=${filter}`}
            className={`rounded-full border px-3 py-1 text-sm transition ${
              active === filter
                ? "bg-foreground text-background border-foreground"
                : "text-muted-foreground hover:bg-muted"
            }`}
          >
            {filter}
            {countFor(filter) !== null ? <span className="ml-1.5 tabular-nums opacity-70">{countFor(filter)}</span> : null}
          </Link>
        ))}
      </div>

      {offers.length === 0 ? (
        <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
          {active === "All" ? "No storefront offers yet." : `No ${active.toLowerCase()} storefront offers.`}
        </div>
      ) : (
        <div className="bg-card overflow-x-auto rounded-xl border">
          <table className="w-full text-sm">
            <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
              <tr>
                <th className="px-4 py-3 font-medium">Item</th>
                <th className="px-4 py-3 text-right font-medium">Offer</th>
                <th className="px-4 py-3 text-right font-medium">List price</th>
                <th className="px-4 py-3 font-medium">Buyer</th>
                <th className="px-4 py-3 font-medium">Message</th>
                <th className="px-4 py-3 font-medium">Response</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium">Received</th>
                <th className="px-4 py-3 font-medium">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {offers.map((offer) => (
                <tr key={offer.id} className="hover:bg-muted/30 transition-colors">
                  <td className="px-4 py-3">
                    <Link href={`/inventory/${offer.inventoryItemId}`} className="font-medium hover:text-brand-700">
                      {offer.listingTitle}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-right font-semibold tabular-nums">{formatCurrency(offer.offerAmount)}</td>
                  <td className="text-muted-foreground px-4 py-3 text-right tabular-nums">
                    {formatCurrency(offer.listingPriceSnapshot)}
                  </td>
                  <td className="px-4 py-3">
                    <span className="font-medium">{offer.buyerName}</span>
                    <a className="text-muted-foreground mt-1 flex items-center gap-1 text-xs hover:text-foreground" href={`mailto:${offer.buyerEmail}`}>
                      <Mail className="h-3 w-3" aria-hidden />
                      {offer.buyerEmail}
                    </a>
                    {offer.buyerPhone ? (
                      <a className="text-muted-foreground mt-1 flex items-center gap-1 text-xs hover:text-foreground" href={`tel:${offer.buyerPhone}`}>
                        <Phone className="h-3 w-3" aria-hidden />
                        {offer.buyerPhone}
                      </a>
                    ) : null}
                  </td>
                  <td className="text-muted-foreground max-w-sm px-4 py-3">
                    {offer.message || "—"}
                  </td>
                  <td className="text-muted-foreground max-w-sm px-4 py-3">
                    {offer.responseAmount != null ? (
                      <span className="text-foreground block font-medium">{formatCurrency(offer.responseAmount)}</span>
                    ) : null}
                    {offer.responseMessage || "—"}
                    {offer.expiresAt ? <span className="block text-xs">Expires {date(offer.expiresAt)}</span> : null}
                  </td>
                  <td className="px-4 py-3">
                    <span className="bg-muted rounded-full px-2 py-1 text-xs font-medium">{offer.status}</span>
                  </td>
                  <td className="text-muted-foreground px-4 py-3">{date(offer.offeredAt)}</td>
                  <td className="min-w-80 px-4 py-3">
                    <div className="space-y-2">
                      <form action={decideOffer} className="flex flex-wrap gap-2">
                        <input type="hidden" name="offerId" value={offer.id} />
                        <input type="hidden" name="decision" value="accept" />
                        <input
                          name="responseMessage"
                          placeholder="Buyer-visible note"
                          className="border-input bg-background h-9 min-w-44 rounded-md border px-3 text-sm"
                        />
                        <button className="bg-brand-600 h-9 rounded-md px-3 text-sm font-medium text-white" type="submit">
                          Accept
                        </button>
                      </form>
                      <form action={decideOffer} className="flex flex-wrap gap-2">
                        <input type="hidden" name="offerId" value={offer.id} />
                        <input type="hidden" name="decision" value="counter" />
                        <input
                          name="responseAmount"
                          type="number"
                          min="0.01"
                          step="0.01"
                          placeholder="Counter"
                          className="border-input bg-background h-9 w-28 rounded-md border px-3 text-sm"
                        />
                        <input
                          name="responseMessage"
                          placeholder="Buyer-visible note"
                          className="border-input bg-background h-9 min-w-44 rounded-md border px-3 text-sm"
                        />
                        <button className="bg-primary h-9 rounded-md px-3 text-sm font-medium text-primary-foreground" type="submit">
                          Counter
                        </button>
                      </form>
                      <form action={decideOffer} className="flex flex-wrap gap-2">
                        <input type="hidden" name="offerId" value={offer.id} />
                        <input type="hidden" name="decision" value="decline" />
                        <input
                          name="responseMessage"
                          placeholder="Reason / note"
                          className="border-input bg-background h-9 min-w-44 rounded-md border px-3 text-sm"
                        />
                        <button className="border-destructive/40 text-destructive h-9 rounded-md border px-3 text-sm font-medium" type="submit">
                          Decline
                        </button>
                      </form>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
