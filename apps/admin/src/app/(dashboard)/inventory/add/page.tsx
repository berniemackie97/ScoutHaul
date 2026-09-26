import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { getApiClient } from "@/lib/api";
import { AddInventory } from "./add-inventory";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

type Mode = "single" | "lot" | "bundle";
const MODES = new Set<Mode>(["single", "lot", "bundle"]);

export default async function AddInventoryPage({ searchParams }: { searchParams: Promise<{ mode?: string }> }) {
  const refused = await refusal("/inventory/add");
  if (refused) return refused;

  const { mode } = await searchParams;
  const initialMode = (MODES.has(mode as Mode) ? mode : "single") as Mode;

  const api = await getApiClient();
  const [{ data: referenceData }, { data: inventory }] = await Promise.all([
    api.GET("/api/inventory/reference-data"),
    api.GET("/api/inventory", { params: { query: { includeArchived: false } } }),
  ]);
  if (!referenceData) throw new Error("Inventory reference data could not be loaded.");

  const items = (inventory ?? []) as InventoryItem[];
  const bundleCandidates = items.filter((item) => item.status !== "Bundled" && !item.isSold && item.status !== "Listed");

  return (
    <div className="space-y-6">
      <div>
        <Link href="/inventory" className="text-muted-foreground inline-flex items-center gap-1 text-sm hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Inventory
        </Link>
        <h1 className="mt-2 text-2xl font-semibold tracking-tight">Add inventory</h1>
        <p className="text-muted-foreground mt-1 text-sm">One place to add a single item, record a purchase lot to itemize, or bundle existing items.</p>
      </div>
      <AddInventory referenceData={referenceData} bundleCandidates={bundleCandidates} initialMode={initialMode} />
    </div>
  );
}
