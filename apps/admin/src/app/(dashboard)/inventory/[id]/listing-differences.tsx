"use client";

import { useState, useTransition } from "react";
import { RefreshCw } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import { takeListingDifference } from "./listing-difference-actions";

type ListingDifference = components["schemas"]["ListingDifferenceDto"];

const FIELD_LABELS: Record<string, string> = { Category: "Category", Condition: "Condition", Brand: "Brand", Colors: "Colors", Size: "Size" };

const keyOf = (difference: ListingDifference) => `${difference.listingId}:${difference.field}`;

/**
 * Where linked marketplace listings disagree with the item. Sync only fills blanks, so these wait for a person:
 * take the listing's value, or keep the item's. Keeping is for this view only; nothing is stored.
 */
export function ListingDifferences({ itemId, initial }: { itemId: string; initial: ListingDifference[] }) {
  const [differences, setDifferences] = useState(initial);
  const [kept, setKept] = useState<Set<string>>(new Set());
  const [message, setMessage] = useState("");
  const [pending, startTransition] = useTransition();
  const open = differences.filter((difference) => !kept.has(keyOf(difference)));

  if (differences.length === 0) return null;

  function take(difference: ListingDifference) {
    startTransition(async () => {
      const result = await takeListingDifference(itemId, difference.listingId!, difference.field!);
      setMessage(result.message);
      if (result.differences) setDifferences(result.differences);
    });
  }

  return (
    <Card id="listing-differences" className="border-warning-border">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <RefreshCw className="h-4 w-4 text-warning" aria-hidden /> Listings disagree with this item
        </CardTitle>
        <CardDescription>Read in your own terms first, so spelling differences never show. Taking a new category also clears details the new kind does not have.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {open.length === 0 && <p className="text-muted-foreground text-sm">All reviewed. Kept values stay until the next visit.</p>}
        {open.map((difference) => (
          <div key={keyOf(difference)} className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-lg border p-3 text-sm">
            <div className="min-w-32">
              <div className="font-medium">{FIELD_LABELS[difference.field ?? ""] ?? difference.field}</div>
              <div className="text-muted-foreground text-xs">
                {difference.listingUrl ? (
                  <a href={difference.listingUrl} target="_blank" rel="noreferrer" className="hover:underline">{difference.platform}</a>
                ) : (
                  difference.platform
                )}
              </div>
            </div>
            <div className="min-w-0 flex-1">
              <span className="text-muted-foreground">{difference.current}</span>
              <span aria-hidden className="text-muted-foreground mx-2">→</span>
              <span className="font-semibold">{difference.incoming}</span>
              {difference.raw && <span className="text-muted-foreground ml-2 text-xs">listing says “{difference.raw}”</span>}
            </div>
            <div className="flex gap-2">
              <Button type="button" size="sm" variant="outline" disabled={pending} onClick={() => setKept(new Set(kept).add(keyOf(difference)))}>
                Keep mine
              </Button>
              <Button type="button" size="sm" disabled={pending} onClick={() => take(difference)}>
                Take {difference.platform}&rsquo;s
              </Button>
            </div>
          </div>
        ))}
        <p aria-live="polite" className="text-muted-foreground text-xs">{message}</p>
      </CardContent>
    </Card>
  );
}
