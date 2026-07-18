import Link from "next/link";
import { Handshake, Mail, Phone, Tags } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Card } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { decideOffer } from "./actions";

export const dynamic = "force-dynamic";

type StorefrontOffer = components["schemas"]["StorefrontOfferDto"];

const FILTERS = ["All", "New", "Accepted", "Countered", "Declined", "Expired", "Converted"] as const;
type OfferStatusFilter = Exclude<(typeof FILTERS)[number], "All">;

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value)) : "—";

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
  const { data } = await api.GET("/api/admin/storefront/offers", {
    params: { query: active === "All" ? {} : { status: active as OfferStatusFilter } },
  });
  const offers = (data ?? []) as StorefrontOffer[];

  const newOffers = offers.filter((offer) => offer.status === "New").length;
  const totalOffered = offers.reduce((sum, offer) => sum + (offer.offerAmount ?? 0), 0);
  const averageOffer =
    offers.length > 0 ? totalOffered / offers.length : 0;

  const kpis = [
    { label: "Offers in view", value: String(offers.length), detail: active === "All" ? "All statuses" : active, icon: Handshake },
    { label: "New offers", value: String(newOffers), detail: "Needs operator reply", icon: Mail },
    { label: "Average offer", value: money(averageOffer), detail: "Buyer proposed value", icon: Tags },
  ];

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

      <div className="grid gap-4 sm:grid-cols-3">
        {kpis.map((kpi) => (
          <Card key={kpi.label} className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-muted-foreground text-sm">{kpi.label}</span>
              <kpi.icon className="text-muted-foreground h-4 w-4" aria-hidden />
            </div>
            <p className="mt-3 text-3xl font-semibold tabular-nums">{kpi.value}</p>
            <p className="text-muted-foreground mt-1 text-xs">{kpi.detail}</p>
          </Card>
        ))}
      </div>

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
                  <td className="px-4 py-3 text-right font-semibold tabular-nums">{money(offer.offerAmount)}</td>
                  <td className="text-muted-foreground px-4 py-3 text-right tabular-nums">
                    {money(offer.listingPriceSnapshot)}
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
                      <span className="text-foreground block font-medium">{money(offer.responseAmount)}</span>
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
