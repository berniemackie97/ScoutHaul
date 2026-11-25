"use client";

import { useActionState, useState } from "react";
import { useFormStatus } from "react-dom";
import { Plus, Trash2 } from "lucide-react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input } from "@overhaul/ui";
import { addLotItems, type LotActionResult } from "./actions";

type AllocationMethod = "Equal" | "WeightedByExpectedValue" | "Manual";
type Row = { key: string; title: string; category: string; condition: string; expected: string; manualCost: string; weight: string };

const initial: LotActionResult = { ok: false, message: "" };
const emptyRow = (): Row => ({ key: crypto.randomUUID(), title: "", category: "", condition: "", expected: "", manualCost: "", weight: "" });
const num = (value: string): number | null => {
  const parsed = Number(value.trim());
  return value.trim() && Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
};

function SubmitButton({ count }: { count: number }) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending || count === 0}>
      {pending ? "Adding items…" : count === 0 ? "Add items" : `Add ${count} item${count === 1 ? "" : "s"} to lot`}
    </Button>
  );
}

export function ItemizeForm({ lotId, allocationMethod }: { lotId: string; allocationMethod: AllocationMethod }) {
  const boundAction = addLotItems.bind(null, lotId);
  const [state, action] = useActionState(boundAction, initial);
  const [rows, setRows] = useState<Row[]>([emptyRow()]);

  const showManual = allocationMethod === "Manual";
  const showWeight = allocationMethod === "WeightedByExpectedValue";

  // Serialize named rows into a single structured payload the server action parses (no positional coupling).
  const payload = rows
    .filter((row) => row.title.trim().length > 0)
    .map((row) => ({
      title: row.title.trim(),
      category: row.category.trim() || null,
      condition: row.condition.trim() || null,
      estimatedSalePrice: num(row.expected),
      manualAllocatedCost: showManual ? num(row.manualCost) : null,
      allocationWeight: showWeight ? num(row.weight) : null,
    }));

  function update(key: string, field: keyof Row, value: string) {
    setRows((current) => current.map((row) => (row.key === key ? { ...row, [field]: value } : row)));
  }
  function addRow() {
    setRows((current) => [...current, emptyRow()]);
  }
  function removeRow(key: string) {
    setRows((current) => (current.length === 1 ? current : current.filter((row) => row.key !== key)));
  }

  const methodWord = allocationMethod === "WeightedByExpectedValue" ? "weighted" : allocationMethod.toLowerCase();

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Itemize this lot</CardTitle>
        <CardDescription>
          Create inventory records directly out of the lot. New items are added as <strong>Purchased</strong> and the lot&rsquo;s landed cost
          re-allocates across every item using the <strong>{methodWord}</strong> method.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-4">
          <input type="hidden" name="itemsJson" value={JSON.stringify(payload)} />
          <div className="overflow-x-auto rounded-lg border">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 font-medium">Item title</th>
                  <th className="px-3 py-2 font-medium">Category</th>
                  <th className="px-3 py-2 font-medium">Condition</th>
                  <th className="px-3 py-2 font-medium">Expected sale</th>
                  {showManual && <th className="px-3 py-2 font-medium">Manual cost</th>}
                  {showWeight && <th className="px-3 py-2 font-medium">Weight</th>}
                  <th className="px-3 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y">
                {rows.map((row) => (
                  <tr key={row.key}>
                    <td className="px-3 py-2">
                      <Input value={row.title} onChange={(event) => update(row.key, "title", event.target.value)} placeholder="e.g. Charizard holo, PSA-ready" />
                    </td>
                    <td className="px-3 py-2">
                      <Input value={row.category} onChange={(event) => update(row.key, "category", event.target.value)} placeholder="Trading cards" />
                    </td>
                    <td className="px-3 py-2">
                      <Input value={row.condition} onChange={(event) => update(row.key, "condition", event.target.value)} placeholder="Near mint" />
                    </td>
                    <td className="px-3 py-2">
                      <Input type="number" min="0" step="0.01" value={row.expected} onChange={(event) => update(row.key, "expected", event.target.value)} />
                    </td>
                    {showManual && (
                      <td className="px-3 py-2">
                        <Input type="number" min="0" step="0.01" value={row.manualCost} onChange={(event) => update(row.key, "manualCost", event.target.value)} />
                      </td>
                    )}
                    {showWeight && (
                      <td className="px-3 py-2">
                        <Input type="number" min="0" step="0.01" value={row.weight} onChange={(event) => update(row.key, "weight", event.target.value)} />
                      </td>
                    )}
                    <td className="px-3 py-2 text-right">
                      <button type="button" onClick={() => removeRow(row.key)} className="text-muted-foreground hover:text-destructive disabled:opacity-40" aria-label="Remove row" disabled={rows.length === 1}>
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <button type="button" onClick={addRow} className="inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm font-medium hover:bg-muted">
              <Plus className="h-4 w-4" /> Add another item
            </button>
            <SubmitButton count={payload.length} />
          </div>
          {state.message && (
            <div className={`rounded-lg border p-3 text-sm ${state.ok ? "border-success-border bg-success-surface text-success" : "border-destructive-border bg-destructive-surface text-destructive"}`}>
              {state.message}
            </div>
          )}
        </form>
      </CardContent>
    </Card>
  );
}
