"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { ExternalLink, Radio, Trash2 } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Card, CardContent, CardDescription, CardHeader, CardTitle, Input } from "@overhaul/ui";
import { upsertChannelListing, removeChannelListing, type ListingActionResult } from "./channel-listing-actions";

type Account = components["schemas"]["ChannelAccountDto"];
type Listing = components["schemas"]["ChannelListingDto"];

const STATUSES = ["NotListed", "Draft", "Active", "Sold", "PendingDelist", "Delisted", "Error"] as const;
const STATUS_LABELS: Record<(typeof STATUSES)[number], string> = {
  NotListed: "Not listed",
  Draft: "Draft",
  Active: "Active",
  Sold: "Sold",
  PendingDelist: "Manual delist needed",
  Delisted: "Delisted",
  Error: "Needs review",
};
const selectClass = "h-9 rounded-lg border border-input bg-background px-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring";
const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "";

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
  const [status, setStatus] = useState<string>(listing?.status ?? "NotListed");

  function save(formData: FormData) {
    formData.set("channelAccountId", account.id ?? "");
    formData.set("status", status);
    startTransition(async () => {
      const outcome = await upsertChannelListing(itemId, { ok: false, message: "" }, formData);
      setMessage(outcome);
      if (outcome.ok) router.refresh();
    });
  }

  function remove() {
    if (!listing?.id) return;
    startTransition(async () => {
      const outcome = await removeChannelListing(itemId, listing.id!);
      setMessage(outcome);
      if (outcome.ok) router.refresh();
    });
  }

  return (
    <div className="rounded-lg border p-3">
      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <span className="min-w-0 break-words font-medium">{account.displayName}</span>
          <Badge variant="secondary">{account.channel}</Badge>
          {account.isNativelySynced && <Badge variant="success">Native</Badge>}
        </div>
        <div className="flex items-center gap-2">
          {listing && <Badge variant={statusVariant(listing.status)}>{statusLabel(listing.status)}</Badge>}
          {listing?.externalUrl && (
            <a href={listing.externalUrl} target="_blank" rel="noreferrer" className="text-muted-foreground hover:text-foreground" title="Open listing">
              <ExternalLink className="h-4 w-4" />
            </a>
          )}
          {listing && (
            <button type="button" onClick={remove} disabled={pending} className="text-muted-foreground hover:text-destructive disabled:opacity-40" title="Remove tracking row" aria-label="Remove tracking row">
              <Trash2 className="h-4 w-4" />
            </button>
          )}
        </div>
      </div>
      <form action={save} className="mt-3 space-y-2">
        <div className="grid gap-2 sm:grid-cols-2">
          <select value={status} onChange={(event) => setStatus(event.target.value)} className={selectClass} aria-label="Status">
            {STATUSES.map((option) => (
              <option key={option} value={option}>
                {STATUS_LABELS[option]}
              </option>
            ))}
          </select>
          <Input name="listedPrice" type="number" step="0.01" min="0" placeholder="Price" defaultValue={money(listing?.listedPrice).replace("$", "") || undefined} className="h-9" />
        </div>
        <Input name="externalUrl" placeholder="Listing URL" defaultValue={listing?.externalUrl ?? undefined} className="h-9" />
        <Input name="externalListingId" placeholder="Marketplace listing ID / SKU (optional)" defaultValue={listing?.externalListingId ?? undefined} className="h-9" />
        <button type="submit" disabled={pending} className="bg-brand-600 hover:bg-brand-700 h-9 w-full rounded-lg px-3 text-sm font-medium text-white disabled:opacity-50">
          {pending ? "…" : "Save"}
        </button>
      </form>
      {message && <p className={`mt-2 text-xs ${message.ok ? "text-green-700" : "text-destructive"}`}>{message.message}</p>}
    </div>
  );
}

export function ChannelPresencePanel({ itemId, accounts, listings }: { itemId: string; accounts: Account[]; listings: Listing[] }) {
  const byAccount = new Map(listings.map((listing) => [listing.channelAccountId, listing]));
  const activeCount = listings.filter((listing) => listing.status === "Active").length;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Radio className="h-4 w-4" /> Channel presence
        </CardTitle>
        <CardDescription>
          {accounts.length === 0 ? (
            <>No channels connected. Add accounts on the <a href="/channels" className="text-brand-700 hover:underline">Channels</a> page first.</>
          ) : (
            <>Tracked as active on {activeCount} of {accounts.length} connected channel{accounts.length === 1 ? "" : "s"}. Record where this item is listed or sold; marketplace actions remain manual.</>
          )}
        </CardDescription>
      </CardHeader>
      {accounts.length > 0 && (
        <CardContent className="space-y-3">
          {accounts.map((account) => (
            <Row key={account.id} itemId={itemId} account={account} listing={byAccount.get(account.id)} />
          ))}
        </CardContent>
      )}
    </Card>
  );
}
