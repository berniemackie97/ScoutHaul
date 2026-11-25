import Link from "next/link";
import type { components } from "@overhaul/api-client";
import { formatCurrency } from "@overhaul/ui";
import { daysHeld } from "@/features/inventory/inventory-rows";
import { getInventoryCost, getProjectedMetrics, type InventoryItem } from "@/features/inventory/inventory-operations";

type Sale = components["schemas"]["SaleDto"];
type Listing = components["schemas"]["ChannelListingDto"];
type StoreListing = components["schemas"]["StoreListingDto"];

/** The handful of numbers someone opens an item to see, in one row instead of spread over the sidebar. */
export function ItemFacts({ item, sale, listings, storeListing }: { item: InventoryItem; sale?: Sale; listings: Listing[]; storeListing?: StoreListing | null }) {
  const cost = getInventoryCost(item);
  const { profit: projected, roiPercent } = getProjectedMetrics(item);
  const sold = Boolean(item.isSold && sale);
  // The storefront is one of the places an item is for sale, as the inventory list's channel chips show it.
  const onStore = storeListing?.status === "Active";
  const live = listings.filter((listing) => listing.status === "Active").length + (onStore ? 1 : 0);
  const takeDown = listings.filter((listing) => listing.status === "PendingDelist").length;
  const acquired = item.purchasedAt ?? item.createdAt;
  const held = daysHeld(acquired, sold && sale?.soldAt ? Date.parse(sale.soldAt) : undefined);

  const facts: Array<{ label: string; value: React.ReactNode; detail?: React.ReactNode; tone?: "warning" | "bad" }> = [
    {
      label: "Cost",
      value: formatCurrency(cost),
      detail: item.acquisitionLotId ? (
        <Link href={`/purchases/${item.acquisitionLotId}`} className="hover:text-foreground underline-offset-2 hover:underline">
          {item.acquisitionLotName ?? "From a lot"}
        </Link>
      ) : cost == null ? "Not recorded" : "What you paid",
      tone: cost == null ? "warning" : undefined,
    },
    sold
      ? { label: "Sold for", value: formatCurrency(sale!.soldPrice), detail: `on ${sale!.platform}` }
      : { label: "Price", value: formatCurrency(item.estimatedSalePrice), detail: item.estimatedSalePrice == null ? "Not set" : "Expected", tone: item.estimatedSalePrice == null ? "warning" : undefined },
    sold
      ? { label: "Profit", value: formatCurrency(sale!.netProfit), detail: "Net, after fees and shipping", tone: (sale!.netProfit ?? 0) < 0 ? "bad" : undefined }
      : {
          label: "Profit",
          value: formatCurrency(projected),
          detail: roiPercent == null ? "Projected" : `Projected · ${roiPercent.toFixed(0)}% ROI`,
          tone: projected != null && projected < 0 ? "bad" : undefined,
        },
    {
      label: sold ? "Took" : "Held",
      value: held == null ? "—" : `${held} day${held === 1 ? "" : "s"}`,
      detail: sold ? "From buying to selling" : "Since you bought it",
      tone: !sold && held != null && held > 90 ? "warning" : undefined,
    },
    {
      label: "Location",
      value: item.storageLocation ?? "Not set",
      detail: item.barcode ? `SKU ${item.barcode}` : "No SKU",
      tone: item.storageLocation ? undefined : "warning",
    },
    {
      label: "Channels",
      value: live > 0 ? `Listed on ${live}` : sold ? "Sold" : "Not listed",
      detail: takeDown > 0 ? `${takeDown} still to take down` : `${listings.length + (storeListing ? 1 : 0)} tracked`,
      tone: takeDown > 0 ? "warning" : undefined,
    },
  ];

  return (
    <dl className="bg-border grid grid-cols-2 gap-px overflow-hidden rounded-xl border sm:grid-cols-3 lg:grid-cols-6">
      {facts.map((fact) => (
        <div key={fact.label} className="bg-card min-w-0 p-4">
          <dt className="text-muted-foreground text-xs">{fact.label}</dt>
          <dd
            className={`mt-1 truncate text-lg font-semibold ${
              fact.tone === "bad" ? "text-destructive" : fact.tone === "warning" ? "text-warning" : ""
            }`}
          >
            {fact.value}
          </dd>
          {fact.detail && <dd className="text-muted-foreground mt-0.5 truncate text-xs">{fact.detail}</dd>}
        </div>
      ))}
    </dl>
  );
}
