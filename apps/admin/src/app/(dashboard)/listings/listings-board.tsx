"use client";

import { useTransition } from "react";
import type { components } from "@overhaul/api-client";
import { Badge, Button } from "@overhaul/ui";
import { reconcileListing, ignoreListing } from "./actions";

type PlatformListing = components["schemas"]["PlatformListingDto"];

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";

const statusVariant = (status: string | undefined) => {
  if (status === "Active") return "success" as const;
  if (status === "Sold") return "info" as const;
  if (["Draft", "Scheduled"].includes(status ?? "")) return "secondary" as const;
  return "warning" as const; // Ended / Unsold / OutOfStock
};

function ListingRow({ listing }: { listing: PlatformListing }) {
  const [pending, startTransition] = useTransition();
  const linked = listing.reconcileStatus === "Linked";
  const suggestedId = listing.suggestedInventoryItemId ?? null;

  return (
    <div className="flex flex-wrap items-center gap-3 border-b px-4 py-3 last:border-0">
      <div className="flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-lg border bg-muted">
        {listing.primaryImageUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={listing.primaryImageUrl} alt="" className="h-full w-full object-cover" />
        ) : (
          <span className="text-muted-foreground text-xs">no img</span>
        )}
      </div>
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium">{listing.title}</p>
        <p className="text-muted-foreground text-xs">
          {listing.externalListingId}
          {listing.categoryName ? ` · ${listing.categoryName}` : ""}
          {listing.quantitySold ? ` · ${listing.quantitySold} sold` : ""}
        </p>
      </div>
      <Badge variant={statusVariant(listing.status)}>{listing.status}</Badge>
      <span className="w-20 text-right tabular-nums">{money(listing.price)}</span>

      {linked ? (
        <span className="text-emerald-700">
          → {listing.reconciledInventoryItemTitle || "linked"}
        </span>
      ) : (
        <div className="flex items-center gap-2">
          {suggestedId && (
            <Button
              size="sm"
              variant="secondary"
              disabled={pending}
              onClick={() => startTransition(() => reconcileListing(listing.id!, suggestedId))}
              title={listing.suggestedInventoryItemTitle ?? undefined}
            >
              Link{listing.suggestedInventoryItemAlreadySold ? " (sold)" : ""}
            </Button>
          )}
          <Button size="sm" disabled={pending} onClick={() => startTransition(() => reconcileListing(listing.id!, null))}>
            New item
          </Button>
          <button
            type="button"
            disabled={pending}
            onClick={() => startTransition(() => ignoreListing(listing.id!))}
            className="text-muted-foreground hover:text-foreground text-xs underline"
          >
            ignore
          </button>
        </div>
      )}
    </div>
  );
}

export function ListingsBoard({ listings }: { listings: PlatformListing[] }) {
  if (listings.length === 0) {
    return (
      <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
        No scraped listings yet. Sync from an eBay tab with the extension, or import below.
      </div>
    );
  }
  return <div className="bg-card overflow-hidden rounded-xl border">{listings.map((listing) => <ListingRow key={listing.id} listing={listing} />)}</div>;
}
