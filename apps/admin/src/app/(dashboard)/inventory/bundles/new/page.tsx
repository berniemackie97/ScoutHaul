import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import type { InventoryItem } from "@/features/inventory/inventory-operations";
import { getApiClient } from "@/lib/api";
import { BundleCreator } from "./bundle-creator";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

export default async function NewInventoryBundlePage() {
  const refused = await refusal("/inventory/bundles/new");
  if (refused) return refused;

  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory", {
    params: { query: { lifecycle: "Active", includeArchived: false } },
  });

  const candidates = ((data ?? []) as InventoryItem[]).filter((item) =>
    !item.isBundle
    && item.status !== "Bundled"
    && !["Sold", "Packed", "Shipped", "Delivered", "Returned", "Donated", "Disposed", "Listed"].includes(item.status ?? "")
  );

  return (
    <div className="space-y-6">
      <header>
        <Link href="/inventory" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Inventory
        </Link>
        <h1 className="mt-4 text-2xl font-semibold tracking-tight">Build inventory bundle</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          Combine existing available inventory into one sellable bundle record. Component costs are snapshotted and components become unavailable for individual sale.
        </p>
      </header>

      <BundleCreator candidates={candidates} />
    </div>
  );
}
