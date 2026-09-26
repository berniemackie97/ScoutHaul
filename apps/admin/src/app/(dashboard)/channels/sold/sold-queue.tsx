"use client";

import Link from "next/link";
import { useActionState } from "react";
import { ExternalLink } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, formatCurrency, formatDate } from "@overhaul/ui";
import { acknowledgeSold, type SoldResult } from "./actions";

type Sold = components["schemas"]["SoldNotRecordedDto"];

const idle: SoldResult = { ok: true, message: "" };

export function SoldQueue({ listings }: { listings: Sold[] }) {
  if (listings.length === 0) {
    return (
      <Card className="text-muted-foreground p-6 text-sm">
        Every sold listing has a sale behind it. This fills up when a marketplace shows a listing as sold before the
        sale reaches the books — which on Depop is the only word of a sale there is.
      </Card>
    );
  }

  return (
    <div className="grid gap-3">
      {listings.map((listing) => (
        <SoldListing key={listing.platformListingId} listing={listing} />
      ))}
    </div>
  );
}

function SoldListing({ listing }: { listing: Sold }) {
  const [state, submit, pending] = useActionState(acknowledgeSold, idle);

  // The item page records the sale, and it opens knowing what the listing knew.
  const record = new URLSearchParams({ soldOn: listing.platform ?? "", price: String(listing.listedPrice ?? "") });
  if (listing.markedSoldAt) {
    record.set("soldAt", listing.markedSoldAt.slice(0, 10));
  }

  return (
    <Card className="space-y-3 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="text-sm">
          <Link href={`/inventory/${listing.inventoryItemId}`} className="font-medium hover:underline">
            {listing.itemTitle}
          </Link>
          <p className="text-muted-foreground mt-1 text-xs">
            {listing.platform} says it sold
            {listing.markedSoldAt ? ` — first seen ${formatDate(listing.markedSoldAt)}` : ""}
            {typeof listing.listedPrice === "number" ? ` · listed at ${formatCurrency(listing.listedPrice)}` : ""}
          </p>
        </div>
        <Badge variant="warning">No sale recorded</Badge>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Link
          href={`/inventory/${listing.inventoryItemId}?${record.toString()}`}
          className="bg-primary text-primary-foreground hover:bg-primary/90 inline-flex h-9 items-center rounded-md px-3 text-sm font-medium"
        >
          Record the sale
        </Link>

        {listing.listingUrl ? (
          <a
            href={listing.listingUrl}
            target="_blank"
            rel="noreferrer"
            className="hover:bg-muted inline-flex h-9 items-center gap-2 rounded-md border px-3 text-sm"
          >
            <ExternalLink className="h-4 w-4" aria-hidden /> Open on {listing.platform}
          </a>
        ) : null}

        <form action={submit}>
          <input type="hidden" name="platformListingId" value={listing.platformListingId} />
          <Button type="submit" size="sm" variant="outline" disabled={pending}>Already recorded, or not a sale</Button>
        </form>
      </div>

      {state.message ? (
        <p className={`text-xs ${state.ok ? "text-emerald-700" : "text-amber-700"}`} aria-live="polite">
          {state.message}
        </p>
      ) : null}
    </Card>
  );
}
