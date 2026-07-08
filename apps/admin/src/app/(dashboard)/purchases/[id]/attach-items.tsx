"use client";

import { useActionState, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import { Link2 } from "lucide-react";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input } from "@overhaul/ui";
import { attachItemsToLot, type LotActionResult } from "./actions";

const initial: LotActionResult = { ok: false, message: "" };
const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";

function SubmitButton({ count }: { count: number }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending || count === 0}>{pending ? "Attaching…" : `Attach ${count} item${count === 1 ? "" : "s"}`}</Button>;
}

export function AttachItems({ lotId, candidates }: { lotId: string; candidates: InventoryItem[] }) {
  const [state, action] = useActionState(attachItemsToLot.bind(null, lotId), initial);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState("");

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return q ? candidates.filter((item) => (item.title ?? "").toLowerCase().includes(q)) : candidates;
  }, [candidates, query]);

  function toggle(id: string) {
    setSelected((cur) => { const next = new Set(cur); next.has(id) ? next.delete(id) : next.add(id); return next; });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg"><Link2 className="h-4 w-4" /> Attach existing items</CardTitle>
        <CardDescription>Add inventory items that aren&rsquo;t in any lot yet. The landed cost re-allocates across the full set. (Manual-allocation lots need per-item costs set in the editor.)</CardDescription>
      </CardHeader>
      <CardContent>
        {candidates.length === 0 ? (
          <p className="text-muted-foreground text-sm">No unassigned inventory items available to attach.</p>
        ) : (
          <form action={action} className="space-y-3">
            <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search items…" className="h-9 max-w-sm" />
            <div className="max-h-72 overflow-auto rounded-lg border">
              <table className="w-full text-sm">
                <tbody className="divide-y">
                  {filtered.map((item) => {
                    if (!item.id) return null;
                    const checked = selected.has(item.id);
                    return (
                      <tr key={item.id} className={checked ? "bg-brand-50/40" : undefined}>
                        <td className="px-3 py-2">
                          <label className="flex items-start gap-2">
                            <input type="checkbox" name="inventoryItemId" value={item.id} checked={checked} onChange={() => toggle(item.id!)} className="mt-1" />
                            <span><span className="font-medium">{item.title}</span><span className="text-muted-foreground block text-xs">{[item.status, item.category].filter(Boolean).join(" · ")}</span></span>
                          </label>
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">{money(item.purchasePrice)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            <div className="flex items-center gap-3">
              <SubmitButton count={selected.size} />
              {state.message && <span className={`text-sm ${state.ok ? "text-green-700" : "text-destructive"}`}>{state.message}</span>}
            </div>
          </form>
        )}
      </CardContent>
    </Card>
  );
}
