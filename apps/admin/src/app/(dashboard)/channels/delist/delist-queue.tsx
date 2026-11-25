"use client";

import Link from "next/link";
import { useActionState } from "react";
import { ExternalLink } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, formatCurrency, formatDate } from "@overhaul/ui";
import { keepListed, markDelisted, type DelistResult } from "./actions";

type Pending = components["schemas"]["PendingDelistDto"];

const idle: DelistResult = { ok: true, message: "" };

export function DelistQueue({ listings }: { listings: Pending[] }) {
  if (listings.length === 0) {
    return (
      <Card className="text-muted-foreground p-6 text-sm">
        Nothing is still for sale after selling. This fills up when an item sells on one marketplace while its
        listings are live on others.
      </Card>
    );
  }

  return (
    <div className="grid gap-3">
      {listings.map((listing) => (
        <PendingListing key={listing.channelListingId} listing={listing} />
      ))}
    </div>
  );
}

function PendingListing({ listing }: { listing: Pending }) {
  const [removed, submitRemoved, removing] = useActionState(markDelisted, idle);
  const [kept, submitKept, keeping] = useActionState(keepListed, idle);
  const pending = removing || keeping;
  const state = removed.message ? removed : kept;

  return (
    <Card className="space-y-3 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="text-sm">
          <Link href={`/inventory/${listing.inventoryItemId}`} className="font-medium hover:underline">
            {listing.itemTitle}
          </Link>
          <p className="text-muted-foreground mt-1 text-xs">
            Still listed on <span className="text-foreground">{listing.accountName ?? listing.channel}</span>
            {typeof listing.listedPrice === "number" ? ` at ${formatCurrency(listing.listedPrice)}` : ""}
            {listing.soldOnChannel
              ? ` · sold on ${listing.soldOnChannel}${listing.soldAt ? ` ${formatDate(listing.soldAt)}` : ""}`
              : ""}
          </p>
        </div>
        <Badge variant="warning">Can still be bought</Badge>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        {listing.externalUrl ? (
          <a
            href={listing.externalUrl}
            target="_blank"
            rel="noreferrer"
            className="hover:bg-muted inline-flex h-9 items-center gap-2 rounded-md border px-3 text-sm"
          >
            <ExternalLink className="h-4 w-4" aria-hidden /> Open on {listing.channel}
          </a>
        ) : (
          <span className="text-muted-foreground text-xs">No link recorded — find it on {listing.channel}.</span>
        )}

        <form action={submitRemoved}>
          <input type="hidden" name="channelListingId" value={listing.channelListingId} />
          <Button type="submit" size="sm" disabled={pending}>I&rsquo;ve taken it down</Button>
        </form>

        <form action={submitKept}>
          <input type="hidden" name="channelListingId" value={listing.channelListingId} />
          <Button type="submit" size="sm" variant="outline" disabled={pending}>Leave it listed</Button>
        </form>
      </div>

      {state.message ? (
        <p className={`text-xs ${state.ok ? "text-success" : "text-warning"}`} aria-live="polite">
          {state.message}
        </p>
      ) : null}
    </Card>
  );
}
