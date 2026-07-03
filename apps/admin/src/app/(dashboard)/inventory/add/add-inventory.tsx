"use client";

import { useState } from "react";
import { Box, Layers, Package } from "lucide-react";
import type { components } from "@overhaul/api-client";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { ItemCreator } from "../new/item-creator";
import { PurchaseLotCreator } from "../../purchases/purchase-lot-creator";
import { BundleCreator } from "../bundles/new/bundle-creator";

type ReferenceData = components["schemas"]["InventoryReferenceDataDto"];
type Mode = "single" | "lot" | "bundle";

const MODES: { id: Mode; label: string; hint: string; icon: typeof Box }[] = [
  { id: "single", label: "Single item", hint: "One item you bought", icon: Box },
  { id: "lot", label: "Purchase lot", hint: "A haul/receipt to itemize", icon: Package },
  { id: "bundle", label: "Bundle", hint: "Group existing items to sell together", icon: Layers },
];

export function AddInventory({
  referenceData,
  lotCandidates,
  bundleCandidates,
  initialMode = "single",
}: {
  referenceData: ReferenceData;
  lotCandidates: InventoryItem[];
  bundleCandidates: InventoryItem[];
  initialMode?: Mode;
}) {
  const [mode, setMode] = useState<Mode>(initialMode);

  return (
    <div className="space-y-6">
      <div className="grid gap-3 sm:grid-cols-3">
        {MODES.map((option) => {
          const active = option.id === mode;
          return (
            <button
              key={option.id}
              type="button"
              onClick={() => setMode(option.id)}
              className={`flex items-start gap-3 rounded-xl border p-4 text-left transition ${
                active ? "border-brand-500 bg-brand-50/50 ring-brand-200 ring-2" : "hover:bg-muted"
              }`}
            >
              <option.icon className={`mt-0.5 h-5 w-5 ${active ? "text-brand-600" : "text-muted-foreground"}`} />
              <span>
                <span className="block text-sm font-medium">{option.label}</span>
                <span className="text-muted-foreground block text-xs">{option.hint}</span>
              </span>
            </button>
          );
        })}
      </div>

      {mode === "single" && <ItemCreator referenceData={referenceData} />}
      {mode === "lot" && <PurchaseLotCreator candidates={lotCandidates} />}
      {mode === "bundle" && <BundleCreator candidates={bundleCandidates} />}
    </div>
  );
}
