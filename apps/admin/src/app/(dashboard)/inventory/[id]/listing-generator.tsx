"use client";

import { useState, useTransition } from "react";
import { Copy, Sparkles } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { generateListing, type ListingGenResult } from "./listing-actions";

type GeneratedListing = components["schemas"]["GeneratedListingDto"];

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : null;

function CopyButton({ text, label }: { text: string; label: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(text);
          setCopied(true);
          setTimeout(() => setCopied(false), 1500);
        } catch {
          /* clipboard unavailable — ignore */
        }
      }}
      className="inline-flex items-center gap-1 text-xs text-brand-600 hover:underline"
    >
      <Copy className="h-3 w-3" /> {copied ? "Copied" : label}
    </button>
  );
}

export function ListingGenerator({ item }: { item: InventoryItem }) {
  const [state, setState] = useState<ListingGenResult>({ ok: false, message: "" });
  const [isPending, startTransition] = useTransition();
  const listing: GeneratedListing | undefined = state.listing;

  const generatorNotes = [
    item.notes,
    item.listingNotes ? `Listing notes: ${item.listingNotes}` : null,
    item.testingNotes ? `Testing notes: ${item.testingNotes}` : null,
    item.accessories ? `Included accessories: ${item.accessories}` : null,
    item.missingParts ? `Missing parts/flaws: ${item.missingParts}` : null,
  ].filter((value): value is string => Boolean(value?.trim())).join("\n");

  function run() {
    startTransition(async () => {
      const result = await generateListing({
        title: item.title ?? "",
        brand: item.brand,
        model: item.model,
        category: item.category,
        condition: item.condition,
        color: item.color,
        sizeSystem: item.sizeSystem,
        sizeValue: item.sizeValue,
        attributesJson: item.attributesJson,
        notes: generatorNotes || null,
      });
      setState(result);
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg"><Sparkles className="h-4 w-4" /> Listing generator</CardTitle>
        <CardDescription>Generate a marketplace title, description, and a comp-backed suggested price from this item.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <Button onClick={run} disabled={isPending} className="w-full">
          {isPending ? "Generating…" : listing ? "Regenerate listing" : "Generate listing"}
        </Button>

        {state.message && <p className="text-sm text-destructive">{state.message}</p>}

        {listing && (
          <div className="space-y-4 text-sm">
            <div>
              <div className="mb-1 flex items-center justify-between">
                <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Title</span>
                <CopyButton text={listing.listingTitle ?? ""} label="Copy" />
              </div>
              <p className="rounded-lg border bg-muted/20 p-2 font-medium">{listing.listingTitle}</p>
            </div>

            <div>
              <div className="mb-1 flex items-center justify-between">
                <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Description</span>
                <CopyButton text={listing.listingDescription ?? ""} label="Copy" />
              </div>
              <p className="whitespace-pre-wrap rounded-lg border bg-muted/20 p-2 text-muted-foreground">{listing.listingDescription}</p>
            </div>

            <div className="rounded-lg border p-3">
              <div className="flex items-baseline justify-between">
                <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Suggested list price</span>
                <span className="text-xl font-semibold tabular-nums">{money(listing.suggestedListPrice) ?? "—"}</span>
              </div>
              {(listing.estimatedResaleLow != null || listing.estimatedResaleHigh != null) && (
                <p className="mt-1 text-xs text-muted-foreground">
                  Resale range {money(listing.estimatedResaleLow) ?? "?"}–{money(listing.estimatedResaleHigh) ?? "?"}
                </p>
              )}
              {listing.priceRationale && <p className="mt-1 text-xs text-muted-foreground">{listing.priceRationale}</p>}
            </div>

            {(listing.comps?.length ?? 0) > 0 && (
              <div>
                <p className="mb-1 text-xs font-medium uppercase tracking-wide text-muted-foreground">Comps</p>
                <ul className="space-y-1">
                  {(listing.comps ?? []).map((comp, index) => (
                    <li key={index} className="flex items-center justify-between gap-2 rounded border px-2 py-1">
                      <span className="tabular-nums font-medium">{money(comp.price)}</span>
                      <span className="text-xs text-muted-foreground">
                        {comp.status}
                        {comp.condition ? ` · ${comp.condition}` : ""}
                        {comp.note ? ` · ${comp.note}` : ""}
                        {comp.sourceUrl ? (
                          <>
                            {" · "}
                            <a href={comp.sourceUrl} target="_blank" rel="noreferrer" className="text-brand-600 hover:underline">
                              Open
                            </a>
                          </>
                        ) : null}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
