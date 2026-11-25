"use client";

import { useActionState, useState } from "react";
import { ImageIcon } from "lucide-react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Badge, Button, formatCurrency } from "@overhaul/ui";
import {
  ignoreListing,
  reconcileListing,
  unlinkListing,
  type ListingActionResult,
} from "./actions";
import { MatchPanel } from "./match-panel";

type PlatformListing = components["schemas"]["PlatformListingDto"];


const initial: ListingActionResult = { ok: false, message: "" };


const statusVariant = (status: string | undefined) => {
  if (status === "Active") return "success" as const;
  if (status === "Sold") return "info" as const;
  if (["Draft", "Scheduled"].includes(status ?? "")) return "secondary" as const;
  return "warning" as const; // Ended / Unsold / OutOfStock
};

function LinkButton({ disabled }: { disabled: boolean }) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" name="intent" value="link" size="sm" disabled={pending || disabled}>
      {pending ? "Linking…" : "Link"}
    </Button>
  );
}

function CreateButton() {
  const { pending } = useFormStatus();
  return (
    <Button
      type="submit"
      name="intent"
      value="create"
      size="sm"
      variant="secondary"
      disabled={pending}
    >
      {pending ? "Creating…" : "Create item from listing"}
    </Button>
  );
}

function SmallSubmitButton({
  label,
  pendingLabel = "…",
  variant = "ghost",
}: {
  label: string;
  pendingLabel?: string;
  variant?: "ghost" | "secondary";
}) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" size="sm" variant={variant} disabled={pending}>
      {pending ? pendingLabel : label}
    </Button>
  );
}

function Message({ state }: { state: ListingActionResult }) {
  if (!state.message) return null;
  return (
    <p
      className={`rounded-lg border px-3 py-2 text-xs ${
        state.ok
          ? "border-success-border bg-success-surface text-success"
          : "border-destructive-border bg-destructive-surface text-destructive"
      }`}
    >
      {state.message}
    </p>
  );
}

function ListingRow({ listing }: { listing: PlatformListing }) {
  const [reconcileState, reconcileAction] = useActionState(reconcileListing, initial);
  const [ignoreState, ignoreAction] = useActionState(ignoreListing, initial);
  const [unlinkState, unlinkAction] = useActionState(unlinkListing, initial);
  const linked = listing.reconcileStatus === "Linked";
  const ignored = listing.reconcileStatus === "Ignored";
  const suggestion = listing.suggestedInventoryItemId
    ? { id: listing.suggestedInventoryItemId, title: listing.suggestedInventoryItemTitle ?? "Suggested item", sold: listing.suggestedInventoryItemAlreadySold ?? false }
    : null;
  const [selected, setSelected] = useState<{ id: string; title: string; sold: boolean } | null>(suggestion);
  const [finding, setFinding] = useState(false);

  return (
    <div className="space-y-4 border-b p-4 last:border-0">
      <div className="flex flex-wrap items-start gap-3">
        <div className="bg-muted flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-md border">
          {listing.primaryImageUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={listing.primaryImageUrl} alt="" className="h-full w-full object-cover" />
          ) : (
            <ImageIcon className="text-muted-foreground h-5 w-5" aria-hidden />
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={statusVariant(listing.status)}>{listing.status}</Badge>
            <Badge variant="outline">{listing.platform}</Badge>
            <span className="font-medium tabular-nums">{formatCurrency(listing.price)}</span>
          </div>
          <p className="mt-1.5 font-medium">{listing.title}</p>
          <p className="text-muted-foreground mt-0.5 text-xs">
            {[listing.externalListingId, listing.categoryName, listing.quantitySold ? `${listing.quantitySold} sold` : null]
              .filter(Boolean)
              .join(" · ")}
          </p>
          {linked && <p className="text-success mt-2 text-sm">Linked to {listing.reconciledInventoryItemTitle || "inventory item"}</p>}
        </div>

        {ignored ? (
          <form action={unlinkAction} className="flex items-center gap-2">
            <input type="hidden" name="listingId" value={listing.id ?? ""} />
            <span className="text-muted-foreground text-sm">Ignored</span>
            <SmallSubmitButton label="Restore to queue" variant="secondary" />
          </form>
        ) : linked ? (
          <form action={unlinkAction}>
            <input type="hidden" name="listingId" value={listing.id ?? ""} />
            <SmallSubmitButton label="Unlink" pendingLabel="Unlinking…" />
          </form>
        ) : (
          <div className="flex flex-wrap items-center justify-end gap-2">
            <Button type="button" size="sm" variant="outline" onClick={() => setFinding((open) => !open)} aria-expanded={finding}>
              {finding ? "Hide matches" : "Find match"}
            </Button>
            <form action={reconcileAction}>
              <input type="hidden" name="listingId" value={listing.id ?? ""} />
              <CreateButton />
            </form>
            <form action={ignoreAction}>
              <input type="hidden" name="listingId" value={listing.id ?? ""} />
              <SmallSubmitButton label="Not inventory" />
            </form>
          </div>
        )}
      </div>

      {!linked && !ignored && (
        <>
          {finding && listing.id && (
            <MatchPanel listingId={listing.id} selectedId={selected?.id ?? ""} onSelect={(candidate) => setSelected({ id: candidate.inventoryItemId!, title: candidate.title ?? "Item", sold: candidate.status === "Sold" || candidate.status === "Delivered" })} />
          )}
          {selected && (
            <form action={reconcileAction} className="bg-muted/40 flex flex-wrap items-center gap-3 rounded-lg border px-3 py-2">
              <input type="hidden" name="listingId" value={listing.id ?? ""} />
              <input type="hidden" name="inventoryItemId" value={selected.id} />
              <span className="min-w-0 flex-1 text-sm">
                {selected === suggestion ? "Suggested: " : "Selected: "}
                <span className="font-medium">{selected.title}</span>
                {selected.sold && <span className="text-warning ml-2">already sold — links a past listing</span>}
              </span>
              <LinkButton disabled={false} />
            </form>
          )}
        </>
      )}

      <Message state={reconcileState} />
      <Message state={ignoreState} />
      <Message state={unlinkState} />
    </div>
  );
}

export function ListingsBoard({ listings, emptyMessage }: { listings: PlatformListing[]; emptyMessage: string }) {
  if (listings.length === 0) {
    return (
      <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
        {emptyMessage}
      </div>
    );
  }
  return (
    <div className="bg-card overflow-hidden rounded-xl border">
      {listings.map((listing) => (
        <ListingRow key={listing.id} listing={listing} />
      ))}
    </div>
  );
}
