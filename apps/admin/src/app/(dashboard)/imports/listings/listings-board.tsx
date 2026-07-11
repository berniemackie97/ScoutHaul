"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Input } from "@overhaul/ui";
import {
  ignoreListing,
  reconcileListing,
  unlinkListing,
  type ListingActionResult,
} from "./actions";

type PlatformListing = components["schemas"]["PlatformListingDto"];

export type ListingMatchTarget = {
  id: string;
  title: string;
  isSold: boolean;
  category?: string | null;
};

const initial: ListingActionResult = { ok: false, message: "" };
const selectClass =
  "h-9 w-full rounded-lg border border-input bg-background px-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring";

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

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
      {pending ? "Linking…" : "Link selected item"}
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
      {pending ? "Creating…" : "New item from listing"}
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
          ? "border-emerald-500/30 bg-emerald-500/5 text-emerald-700"
          : "border-destructive/30 bg-destructive/5 text-destructive"
      }`}
    >
      {state.message}
    </p>
  );
}

function ListingRow({
  listing,
  targets,
}: {
  listing: PlatformListing;
  targets: ListingMatchTarget[];
}) {
  const [reconcileState, reconcileAction] = useActionState(reconcileListing, initial);
  const [ignoreState, ignoreAction] = useActionState(ignoreListing, initial);
  const [unlinkState, unlinkAction] = useActionState(unlinkListing, initial);
  const [query, setQuery] = useState("");
  const [selectedId, setSelectedId] = useState(
    listing.reconciledInventoryItemId ?? listing.suggestedInventoryItemId ?? ""
  );

  const linked = listing.reconcileStatus === "Linked";
  const ignored = listing.reconcileStatus === "Ignored";
  const suggestedId = listing.suggestedInventoryItemId ?? null;

  const sortedTargets = useMemo(
    () =>
      [...targets].sort(
        (a, b) => Number(a.isSold) - Number(b.isSold) || a.title.localeCompare(b.title)
      ),
    [targets]
  );

  const filteredTargets = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    const source = normalized
      ? sortedTargets.filter((target) =>
          [target.title, target.category].some((value) => value?.toLowerCase().includes(normalized))
        )
      : sortedTargets;
    return source.slice(0, 100);
  }, [query, sortedTargets]);

  const selectedTarget = targets.find((target) => target.id === selectedId);
  const suggestedTarget = suggestedId ? targets.find((target) => target.id === suggestedId) : null;

  return (
    <div className="grid gap-4 border-b p-4 last:border-0 xl:grid-cols-[minmax(0,1fr)_minmax(360px,520px)]">
      <div className="flex min-w-0 gap-3">
        <div className="bg-muted flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-lg border">
          {listing.primaryImageUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={listing.primaryImageUrl} alt="" className="h-full w-full object-cover" />
          ) : (
            <span className="text-muted-foreground text-xs">no img</span>
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={statusVariant(listing.status)}>{listing.status}</Badge>
            <Badge variant="outline">{listing.platform}</Badge>
            <span className="font-medium tabular-nums">{money(listing.price)}</span>
          </div>
          <p className="mt-2 truncate font-medium">{listing.title}</p>
          <p className="text-muted-foreground mt-1 text-xs">
            {listing.externalListingId}
            {listing.categoryName ? ` · ${listing.categoryName}` : ""}
            {listing.quantitySold ? ` · ${listing.quantitySold} sold` : ""}
          </p>
          {linked && (
            <p className="mt-2 text-sm text-emerald-700">
              Linked to {listing.reconciledInventoryItemTitle || "inventory item"}
            </p>
          )}
          {suggestedTarget && !linked && !ignored && (
            <button
              type="button"
              onClick={() => {
                setSelectedId(suggestedTarget.id);
                setQuery(suggestedTarget.title);
              }}
              className="text-brand-700 mt-2 text-left text-xs hover:underline"
            >
              Suggested: {suggestedTarget.title}
              {suggestedTarget.isSold ? " · already sold" : ""}
            </button>
          )}
        </div>
      </div>

      <div className="space-y-3">
        {ignored ? (
          <form action={unlinkAction} className="flex flex-wrap items-center justify-end gap-2">
            <input type="hidden" name="listingId" value={listing.id ?? ""} />
            <span className="text-muted-foreground mr-auto text-sm">Ignored</span>
            <SmallSubmitButton label="Restore to queue" variant="secondary" />
          </form>
        ) : (
          <>
            <form action={reconcileAction} className="space-y-3">
              <input type="hidden" name="listingId" value={listing.id ?? ""} />
              <input type="hidden" name="inventoryItemId" value={selectedId} />
              <div className="grid gap-2">
                <Input
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  placeholder="Search inventory title or category…"
                  className="h-9"
                />
                <select
                  className={selectClass}
                  value={selectedId}
                  onChange={(event) => setSelectedId(event.target.value)}
                >
                  <option value="">Select inventory item…</option>
                  {filteredTargets.map((target) => (
                    <option key={target.id} value={target.id}>
                      {target.title}
                      {target.isSold ? " · recorded/sold" : ""}
                      {target.id === suggestedId ? " (suggested)" : ""}
                    </option>
                  ))}
                </select>
                {selectedTarget && (
                  <p
                    className={`text-xs ${selectedTarget.isSold ? "text-amber-700" : "text-emerald-700"}`}
                  >
                    {selectedTarget.isSold
                      ? "This links to an already-sold/recorded item. Use this only to fix a historical listing link."
                      : "This links the imported listing to an existing inventory item."}
                  </p>
                )}
              </div>
              <div className="flex flex-wrap justify-end gap-2">
                <LinkButton disabled={!selectedId} />
                {!linked && <CreateButton />}
              </div>
            </form>

            <div className="flex flex-wrap justify-end gap-2">
              {linked ? (
                <form action={unlinkAction}>
                  <input type="hidden" name="listingId" value={listing.id ?? ""} />
                  <SmallSubmitButton label="Unlink" pendingLabel="Unlinking…" />
                </form>
              ) : (
                <form action={ignoreAction}>
                  <input type="hidden" name="listingId" value={listing.id ?? ""} />
                  <SmallSubmitButton label="Ignore" />
                </form>
              )}
            </div>
          </>
        )}

        <Message state={reconcileState} />
        <Message state={ignoreState} />
        <Message state={unlinkState} />
      </div>
    </div>
  );
}

export function ListingsBoard({
  listings,
  targets,
  emptyMessage,
}: {
  listings: PlatformListing[];
  targets: ListingMatchTarget[];
  emptyMessage: string;
}) {
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
        <ListingRow key={listing.id} listing={listing} targets={targets} />
      ))}
    </div>
  );
}
