"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { inventoryStatusLabel, type InventoryItem } from "@/features/inventory/inventory-operations";
import { createBundle, type BundleActionResult } from "./actions";

const initial: BundleActionResult = { ok: false, message: "" };
const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";

function SubmitButton() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Creating bundle…" : "Create bundle"}</Button>;
}

export function BundleCreator({ candidates }: { candidates: InventoryItem[] }) {
  const [state, action] = useActionState(createBundle, initial);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const selectedItems = useMemo(() => candidates.filter((item) => item.id && selected.has(item.id)), [candidates, selected]);
  const cost = selectedItems.reduce((sum, item) => sum + (item.purchasePrice ?? 0), 0);
  const expected = selectedItems.reduce((sum, item) => sum + (item.estimatedSalePrice ?? 0), 0);
  const categories = [...new Set(candidates.map((item) => item.category).filter((value): value is string => Boolean(value)))].sort();

  function toggle(id: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <form action={action} className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
      <div className="space-y-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Bundle listing record</CardTitle>
            <CardDescription>This creates one canonical inventory item representing the bundle. It can then be photographed, listed, sold, shipped, and reported like any other item.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="title">Bundle title</Label>
              <Input id="title" name="title" required placeholder="e.g. Pokémon GX binder lot, 12-card trainer bundle" />
            </div>
            <div className="space-y-2">
              <Label htmlFor="category">Category</Label>
              <select id="category" name="category" className="h-10 w-full rounded-lg border border-input bg-background px-3 text-sm">
                <option value="">Use dominant category</option>
                {categories.map((category) => <option key={category} value={category}>{category}</option>)}
              </select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="status">Starting stage</Label>
              <select id="status" name="status" defaultValue="ReadyToList" className="h-10 w-full rounded-lg border border-input bg-background px-3 text-sm">
                <option value="Draft">Draft</option>
                <option value="NeedsPhotos">Needs photos</option>
                <option value="ReadyToList">Ready to list</option>
              </select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="estimatedSalePrice">Expected bundle sale price</Label>
              <Input id="estimatedSalePrice" name="estimatedSalePrice" type="number" min="0" step="0.01" placeholder={expected > 0 ? String(expected) : undefined} />
            </div>
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="description">Bundle description</Label>
              <Textarea id="description" name="description" rows={4} placeholder="Describe exactly what is included, condition spread, exclusions, and listing notes." />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Components</CardTitle>
            <CardDescription>Select available unlisted inventory. Components will move to Bundled and stop appearing as individual sale candidates.</CardDescription>
          </CardHeader>
          <CardContent>
            {candidates.length === 0 ? (
              <p className="rounded-lg border border-dashed p-10 text-center text-sm text-muted-foreground">No available inventory candidates. Listed/sold/bundled items are intentionally excluded.</p>
            ) : (
              <div className="max-h-[560px] overflow-auto rounded-lg border">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 border-b bg-muted/80 text-left text-xs uppercase tracking-wide text-muted-foreground backdrop-blur">
                    <tr>
                      <th className="px-3 py-2 font-medium">Item</th>
                      <th className="px-3 py-2 font-medium">Stage</th>
                      <th className="px-3 py-2 text-right font-medium">Cost</th>
                      <th className="px-3 py-2 text-right font-medium">Expected</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y">
                    {candidates.map((item) => {
                      if (!item.id) return null;
                      const checked = selected.has(item.id);
                      return (
                        <tr key={item.id} className={checked ? "bg-brand-50/40" : undefined}>
                          <td className="px-3 py-2">
                            <label className="flex items-start gap-2">
                              <input type="checkbox" name="componentInventoryItemId" value={item.id} checked={checked} onChange={() => toggle(item.id!)} className="mt-1" />
                              <span>
                                <span className="font-medium">{item.title}</span>
                                <span className="block text-xs text-muted-foreground">{[item.category, item.brand, item.storageLocation].filter(Boolean).join(" · ") || "No classification"}</span>
                              </span>
                            </label>
                          </td>
                          <td className="px-3 py-2 text-muted-foreground">{inventoryStatusLabel(item.status)}</td>
                          <td className="px-3 py-2 text-right tabular-nums">{money(item.purchasePrice)}</td>
                          <td className="px-3 py-2 text-right tabular-nums">{money(item.estimatedSalePrice)}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle className="text-lg">Internal notes</CardTitle></CardHeader>
          <CardContent><Textarea name="notes" rows={3} placeholder="Why these items were bundled, cross-listing notes, exceptions…" /></CardContent>
        </Card>
      </div>

      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <Card className="p-6">
          <p className="text-sm text-muted-foreground">Bundle cost basis</p>
          <p className="mt-2 text-4xl font-semibold tabular-nums">{money(cost)}</p>
          <div className="mt-5 space-y-2 border-t pt-4 text-sm">
            <div className="flex justify-between"><span className="text-muted-foreground">Components</span><span>{selectedItems.length}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Expected total</span><span>{money(expected)}</span></div>
            <div className="flex justify-between"><span className="text-muted-foreground">Projected spread</span><span>{expected > 0 ? money(expected - cost) : "—"}</span></div>
          </div>
        </Card>
        {state.message && <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">{state.message}</div>}
        <SubmitButton />
      </aside>
    </form>
  );
}
