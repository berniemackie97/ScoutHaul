"use client";

import { useActionState, useEffect, useMemo, useState } from "react";
import { useFormStatus } from "react-dom";
import { Link2 } from "lucide-react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, formatCurrency } from "@overhaul/ui";
import type { PickerItem } from "@/app/api/inventory/picker/route";
import { ItemSearch } from "@/components/inventory/item-search";
import { attachItemsToLot, type LotActionResult } from "./actions";

const initial: LotActionResult = { ok: false, message: "" };

function SubmitButton({ count }: { count: number }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending || count === 0}>{pending ? "Attaching…" : `Attach ${count} item${count === 1 ? "" : "s"}`}</Button>;
}

export function AttachItems({ lotId }: { lotId: string }) {
  const [state, action] = useActionState(attachItemsToLot.bind(null, lotId), initial);
  const [chosen, setChosen] = useState<PickerItem[]>([]);
  const chosenIds = useMemo(() => new Set(chosen.map((item) => item.id)), [chosen]);

  // Attached items are in the lot now; left here they would be offered again and refused.
  useEffect(() => {
    if (state.ok) setChosen([]);
  }, [state]);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg"><Link2 className="h-4 w-4" /> Attach existing items</CardTitle>
        <CardDescription>Add inventory items that aren&rsquo;t in any lot yet. The landed cost re-allocates across the full set. (Manual-allocation lots need per-item costs set in the editor.)</CardDescription>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-3">
          <div className="max-w-xl">
            <ItemSearch<PickerItem>
              label="Search for items to attach to this lot"
              source={(query) => `/api/inventory/picker?for=lot&q=${encodeURIComponent(query)}`}
              idOf={(item) => item.id}
              exclude={chosenIds}
              onPick={(item) => setChosen((current) => [...current, item])}
              render={(item) => (
                <span className="block">
                  <span className="block truncate font-medium">{item.title}</span>
                  <span className="text-muted-foreground block text-xs">{[item.status, item.category].filter(Boolean).join(" · ")}</span>
                </span>
              )}
            />
          </div>
          {chosen.length > 0 ? (
            <div className="rounded-lg border">
              <table className="w-full text-sm">
                <tbody className="divide-y">
                  {chosen.map((item) => (
                    <tr key={item.id}>
                      <td className="px-3 py-2">
                        <input type="hidden" name="inventoryItemId" value={item.id} />
                        <span className="font-medium">{item.title}</span>
                        <span className="text-muted-foreground block text-xs">{[item.status, item.category].filter(Boolean).join(" · ")}</span>
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums">{formatCurrency(item.cost)}</td>
                      <td className="px-3 py-2 text-right">
                        <Button type="button" variant="ghost" size="sm" onClick={() => setChosen((current) => current.filter((other) => other.id !== item.id))}>
                          Remove
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
          <div className="flex items-center gap-3">
            <SubmitButton count={chosen.length} />
            {state.message && <span className={`text-sm ${state.ok ? "text-success" : "text-destructive"}`}>{state.message}</span>}
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
