"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { EyeOff, Plus, RotateCcw, Trash2 } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input } from "@overhaul/ui";
import { addMenuValue, disableMenuValue, removeMenuOverride, type MenuResult } from "./actions";

type ReferenceData = components["schemas"]["InventoryReferenceDataDto"];
type Override = components["schemas"]["OrgReferenceOverrideDto"];

type Kind = { kind: string; label: string; field: keyof ReferenceData };
const KINDS: Kind[] = [
  { kind: "StorageLocation", label: "Storage locations", field: "storageLocations" },
  { kind: "Category", label: "Categories", field: "categories" },
  { kind: "Color", label: "Colors", field: "colors" },
  { kind: "Condition", label: "Conditions", field: "conditions" },
  { kind: "SizeSystem", label: "Size systems", field: "sizeSystems" },
];

export function MenusManager({ referenceData, overrides }: { referenceData: ReferenceData; overrides: Override[] }) {
  const router = useRouter();
  const [active, setActive] = useState<Kind>(KINDS[0]!);
  const [value, setValue] = useState("");
  const [pending, startTransition] = useTransition();
  const [message, setMessage] = useState<MenuResult | null>(null);

  // Flat kinds only: ignore parent-scoped rows.
  const kindOverrides = overrides.filter((o) => o.kind === active.kind && !o.parentValue);
  const byValue = new Map(kindOverrides.map((o) => [o.value?.toLowerCase(), o]));
  const options = ((referenceData[active.field] as { value?: string | null; label?: string | null }[]) ?? []).filter((o) => o.value);
  const hidden = kindOverrides.filter((o) => o.isDisabled);

  function run(fn: () => Promise<MenuResult>) {
    startTransition(async () => {
      const outcome = await fn();
      setMessage(outcome);
      if (outcome.ok) { setValue(""); router.refresh(); }
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Menu options</CardTitle>
        <CardDescription>
          Add your own values or hide ones you don&rsquo;t use. Hiding only removes an option from these dropdowns — it never deletes
          data, and items already using a hidden value keep it.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap gap-2">
          {KINDS.map((k) => (
            <button
              key={k.kind}
              type="button"
              onClick={() => { setActive(k); setMessage(null); }}
              className={`rounded-full border px-3 py-1 text-sm transition ${active.kind === k.kind ? "bg-foreground text-background border-foreground" : "text-muted-foreground hover:bg-muted"}`}
            >
              {k.label}
            </button>
          ))}
        </div>

        <form
          onSubmit={(event) => { event.preventDefault(); run(() => addMenuValue(active.kind, value)); }}
          className="flex gap-2"
        >
          <Input value={value} onChange={(event) => setValue(event.target.value)} placeholder={`Add a ${active.label.toLowerCase().replace(/s$/, "")}…`} className="h-9 max-w-xs" />
          <Button type="submit" disabled={pending || !value.trim()} className="h-9"><Plus className="h-4 w-4" /> Add</Button>
        </form>
        {message && <p className={`text-xs ${message.ok ? "text-green-700" : "text-destructive"}`}>{message.message}</p>}

        <div className="grid gap-2 sm:grid-cols-2">
          {options.map((option) => {
            const override = byValue.get(option.value?.toLowerCase());
            const custom = override?.isCustom;
            return (
              <div key={option.value} className="flex items-center justify-between gap-2 rounded-lg border px-3 py-2 text-sm">
                <span className="flex items-center gap-2">{option.label}{custom && <Badge variant="secondary">Custom</Badge>}</span>
                {custom ? (
                  <button type="button" disabled={pending} onClick={() => run(() => removeMenuOverride(override!.id!))} className="text-muted-foreground hover:text-destructive" title="Remove"><Trash2 className="h-4 w-4" /></button>
                ) : (
                  <button type="button" disabled={pending} onClick={() => run(() => disableMenuValue(active.kind, option.value!))} className="text-muted-foreground hover:text-foreground" title="Hide from menus"><EyeOff className="h-4 w-4" /></button>
                )}
              </div>
            );
          })}
        </div>

        {hidden.length > 0 && (
          <div className="space-y-2 border-t pt-3">
            <p className="text-muted-foreground text-xs font-medium uppercase tracking-wide">Hidden</p>
            <div className="flex flex-wrap gap-2">
              {hidden.map((o) => (
                <button key={o.id} type="button" disabled={pending} onClick={() => run(() => removeMenuOverride(o.id!))} className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 rounded-full border px-3 py-1 text-xs line-through" title="Restore">
                  {o.value} <RotateCcw className="h-3 w-3 no-underline" />
                </button>
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
