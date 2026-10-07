"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { ImageIcon } from "lucide-react";
import { Button, Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, formatCurrency } from "@overhaul/ui";
import { When } from "@/components/when";
import { inventoryStatusLabel } from "@/features/inventory/inventory-operations";
import { presenceLabel, suggestKeeper } from "@/features/inventory/inventory-rows";
import { mergeInventoryItems } from "./actions";
import type { InventoryTableRow } from "./inventory-table";

/** One record kept plus the API's limit of twenty merged into it. */
export const MAX_MERGE = 21;

/** Picks which of the selected records survives, then folds the rest into it and opens it. */
export function MergeDialog({ rows, onClose }: { rows: InventoryTableRow[]; onClose: () => void }) {
  const router = useRouter();
  const [keepId, setKeepId] = useState(() => suggestKeeper(rows)?.id ?? "");
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();
  const kept = rows.find((row) => row.id === keepId);

  function merge() {
    setError(null);
    startTransition(async () => {
      const result = await mergeInventoryItems(keepId, rows.map((row) => row.id!).filter((id) => id !== keepId));
      if (result.ok) router.push(`/inventory/${result.keptId}`);
      else setError(result.message);
    });
  }

  return (
    <Dialog open onOpenChange={(open) => !open && !pending && onClose()}>
      <DialogContent className="w-[calc(100%-2rem)] max-w-2xl grid-cols-[minmax(0,1fr)] rounded-lg">
        <DialogHeader>
          <DialogTitle>Merge {rows.length} records into one</DialogTitle>
          <DialogDescription>
            Pick the record to keep. Its details stand, and the others only fill what it leaves blank. Their sales, photos, listings and history move onto it, then they are deleted. This can&apos;t be undone.
          </DialogDescription>
        </DialogHeader>

        <fieldset className="max-h-[50vh] min-w-0 space-y-2 overflow-y-auto">
          <legend className="sr-only">Record to keep</legend>
          {rows.map((row) => {
            const keep = row.id === keepId;
            const channels = (row.facts?.channels ?? []).map((channel) => presenceLabel(channel).text);
            const cost = row.purchasePrice != null ? `cost ${formatCurrency(row.purchasePrice)}` : "no cost recorded";
            const facts = [inventoryStatusLabel(row.status), ...channels, cost].join(" · ");
            return (
              <label
                key={row.id}
                className={`flex cursor-pointer items-center gap-3 rounded-lg border p-3 transition-colors ${keep ? "border-brand-500 bg-brand-500/5" : "hover:bg-muted/40"}`}
              >
                <input
                  type="radio"
                  name="keep"
                  value={row.id}
                  checked={keep}
                  onChange={() => setKeepId(row.id!)}
                  disabled={pending}
                  aria-label={row.title ?? "Item"}
                  aria-describedby={`merge-facts-${row.id}`}
                  className="h-4 w-4 shrink-0"
                />
                <div className="bg-muted flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-md border">
                  {row.thumbnailUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img src={row.thumbnailUrl} alt="" className="h-full w-full object-cover" loading="lazy" />
                  ) : (
                    <ImageIcon className="text-muted-foreground h-5 w-5" />
                  )}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">{row.title}</p>
                  <p id={`merge-facts-${row.id}`} className="text-muted-foreground text-xs">
                    {facts}
                  </p>
                  {row.createdAt && (
                    <p className="text-muted-foreground text-xs">
                      Added <When value={row.createdAt} />
                    </p>
                  )}
                </div>
                <span className={`shrink-0 text-xs font-medium ${keep ? "text-brand-700 dark:text-brand-300" : "text-muted-foreground"}`}>{keep ? "Keep" : "Merge in"}</span>
              </label>
            );
          })}
        </fieldset>

        {error && <p className="border-destructive/30 bg-destructive/5 text-destructive rounded-lg border p-3 text-sm">{error}</p>}

        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" disabled={pending} onClick={onClose}>
            Cancel
          </Button>
          <Button type="button" disabled={pending || !kept} onClick={merge}>
            {pending ? "Merging…" : `Merge into “${truncate(kept?.title ?? "", 32)}”`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function truncate(text: string, length: number) {
  return text.length > length ? `${text.slice(0, length - 1)}…` : text;
}
