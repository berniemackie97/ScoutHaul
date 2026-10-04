"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { ExternalLink, Pencil, Plus, Radio, Trash2, X } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, formatCurrency } from "@overhaul/ui";
import { upsertChannelListing, removeChannelListing, type ListingActionResult } from "./channel-listing-actions";

type Account = components["schemas"]["ChannelAccountDto"];
type Listing = components["schemas"]["ChannelListingDto"];

const STATUSES = ["NotListed", "Draft", "Active", "Sold", "PendingDelist", "Delisted", "Error"] as const;
const STATUS_LABELS: Record<(typeof STATUSES)[number], string> = {
  NotListed: "Not listed",
  Draft: "Draft",
  Active: "Active",
  Sold: "Sold",
  PendingDelist: "Take down",
  Delisted: "Delisted",
  Error: "Needs review",
};
const selectClass = "h-9 rounded-lg border border-input bg-background px-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring";

const statusVariant = (status: string | undefined) => {
  if (status === "Active") return "success" as const;
  if (status === "Sold") return "info" as const;
  if (status === "Error") return "destructive" as const;
  if (status === "PendingDelist") return "warning" as const;
  if (status === "Delisted") return "outline" as const;
  return "secondary" as const;
};

function statusLabel(status: string | undefined) {
  return STATUSES.includes(status as (typeof STATUSES)[number]) ? STATUS_LABELS[status as (typeof STATUSES)[number]] : (status ?? "Unknown");
}

function Row({ itemId, account, listing }: { itemId: string; account: Account; listing?: Listing }) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [message, setMessage] = useState<ListingActionResult | null>(null);
  const [editing, setEditing] = useState(false);
  const [status, setStatus] = useState<string>(listing?.status ?? "Active");

  function save(formData: FormData) {
    formData.set("channelAccountId", account.id ?? "");
    formData.set("status", status);
    startTransition(async () => {
      const outcome = await upsertChannelListing(itemId, { ok: false, message: "" }, formData);
      setMessage(outcome);
      if (outcome.ok) {
        setEditing(false);
        router.refresh();
      }
    });
  }

  function remove() {
    if (!listing?.id) return;
    startTransition(async () => {
      const outcome = await removeChannelListing(itemId, listing.id!);
      setMessage(outcome);
      if (outcome.ok) {
        setEditing(false);
        router.refresh();
      }
    });
  }

  return (
    <li className="py-3 first:pt-0 last:pb-0">
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate text-sm font-medium">{account.displayName}</p>
          <p className="text-muted-foreground text-xs">{account.channel}{account.isNativelySynced ? " · synced" : ""}</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {listing && listing.status !== "NotListed" ? (
            <>
              {listing.listedPrice != null && <span className="text-sm tabular-nums">{formatCurrency(listing.listedPrice)}</span>}
              <Badge variant={statusVariant(listing.status)}>{statusLabel(listing.status)}</Badge>
              {listing.externalUrl && (
                <a href={listing.externalUrl} target="_blank" rel="noreferrer" className="text-muted-foreground hover:text-foreground" aria-label={`Open on ${account.channel}`}>
                  <ExternalLink className="h-4 w-4" />
                </a>
              )}
            </>
          ) : (
            <span className="text-muted-foreground text-xs">Not listed</span>
          )}
          <button
            type="button"
            onClick={() => setEditing((value) => !value)}
            className="text-muted-foreground hover:text-foreground rounded p-1"
            aria-label={editing ? "Close" : listing ? "Edit listing" : "Record a listing"}
            aria-expanded={editing}
          >
            {editing ? <X className="h-4 w-4" /> : listing ? <Pencil className="h-4 w-4" /> : <Plus className="h-4 w-4" />}
          </button>
        </div>
      </div>
      {editing && (
        <form action={save} className="mt-3 space-y-2 rounded-lg border p-3">
          <div className="grid grid-cols-2 gap-2">
            <select value={status} onChange={(event) => setStatus(event.target.value)} className={selectClass} aria-label="Status">
              {STATUSES.map((option) => (
                <option key={option} value={option}>
                  {STATUS_LABELS[option]}
                </option>
              ))}
            </select>
            <Input name="listedPrice" type="number" step="0.01" min="0" placeholder="Price" defaultValue={listing?.listedPrice ?? undefined} className="h-9" aria-label="Price" />
          </div>
          <Input name="externalUrl" placeholder="Listing link" defaultValue={listing?.externalUrl ?? undefined} className="h-9" aria-label="Listing link" />
          <Input name="externalListingId" placeholder="Marketplace listing id (optional)" defaultValue={listing?.externalListingId ?? undefined} className="h-9" aria-label="Marketplace listing id" />
          <div className="flex items-center justify-between gap-2">
            {listing ? (
              <button type="button" onClick={remove} disabled={pending} className="text-muted-foreground hover:text-destructive inline-flex items-center gap-1 text-xs disabled:opacity-40">
                <Trash2 className="h-3.5 w-3.5" /> Stop tracking
              </button>
            ) : (
              <span />
            )}
            <button type="submit" disabled={pending} className="bg-brand-600 hover:bg-brand-700 h-8 rounded-md px-3 text-sm font-medium text-white disabled:opacity-50">
              {pending ? "Saving…" : "Save"}
            </button>
          </div>
        </form>
      )}
      {message && !message.ok && <p className="text-destructive mt-2 text-xs">{message.message}</p>}
    </li>
  );
}

export function ChannelPresencePanel({ itemId, accounts, listings }: { itemId: string; accounts: Account[]; listings: Listing[] }) {
  const byAccount = new Map(listings.map((listing) => [listing.channelAccountId, listing]));
  const activeCount = listings.filter((listing) => listing.status === "Active").length;

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-lg">
          <Radio className="h-4 w-4" /> Channels
        </CardTitle>
        <CardDescription>
          {accounts.length === 0 ? (
            <>No marketplace accounts yet. Add them on <a href="/channels" className="text-brand-700 hover:underline">Channels</a>.</>
          ) : (
            <>Listed on {activeCount} of {accounts.length}. Listing and delisting happen on the marketplace; this records where it stands.</>
          )}
        </CardDescription>
      </CardHeader>
      {accounts.length > 0 && (
        <CardContent>
          <ul className="divide-y">
            {accounts.map((account) => (
              <Row key={account.id} itemId={itemId} account={account} listing={byAccount.get(account.id)} />
            ))}
          </ul>
        </CardContent>
      )}
    </Card>
  );
}
