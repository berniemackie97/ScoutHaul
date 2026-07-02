import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, Barcode, CalendarDays, MapPin, TrendingUp } from "lucide-react";
import { Badge, Card, CardContent, CardHeader, CardTitle } from "@overhaul/ui";
import {
  getInventoryCost,
  getProjectedMetrics,
  inventoryStatusLabel,
  type InventoryItem,
} from "@/features/inventory/inventory-operations";
import { getApiAssetUrl, getApiClient } from "@/lib/api";
import { InventoryEditor } from "./inventory-editor";
import { PhotoManager, type ManagedPhoto } from "./photo-manager";
import { StatusControl } from "./status-control";
import { ListingGenerator } from "./listing-generator";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

export default async function InventoryDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const api = await getApiClient();
  const [{ data: item }, { data: referenceData }] = await Promise.all([
    api.GET("/api/inventory/{id}", { params: { path: { id } } }),
    api.GET("/api/inventory/reference-data"),
  ]);
  const inventoryItem = item as InventoryItem | undefined;
  if (!inventoryItem?.id || !inventoryItem.status) notFound();
  if (!referenceData) throw new Error("Inventory reference data could not be loaded.");

  const photos: ManagedPhoto[] = (inventoryItem.photos ?? []).flatMap((photo) => {
    const imageUrl = getApiAssetUrl(photo.thumbnailUrl);
    if (!photo.id || !photo.originalUrl || !imageUrl) return [];
    return [{ id: photo.id, imageUrl, apiUrl: photo.originalUrl, isHero: photo.isHero ?? false }];
  });

  const cost = getInventoryCost(inventoryItem);
  const { profit: projectedProfit, roiPercent: projectedRoi } = getProjectedMetrics(inventoryItem);

  return (
    <div className="space-y-6">
      <header className="space-y-4">
        <Link href="/inventory" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Inventory
        </Link>
        <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
          <div>
            <div className="mb-2 flex flex-wrap items-center gap-2">
              <Badge variant="secondary">{inventoryStatusLabel(inventoryItem.status)}</Badge>
              {inventoryItem.isBundle && <Badge variant="info">Bundle</Badge>}
              {inventoryItem.category && <Badge variant="outline">{inventoryItem.category}</Badge>}
            </div>
            <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">{inventoryItem.title}</h1>
            <p className="mt-1 text-sm text-muted-foreground">Canonical inventory record · updated {date(inventoryItem.updatedAt)}</p>
          </div>
          <div className="font-mono text-xs text-muted-foreground">{inventoryItem.id}</div>
        </div>
      </header>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
        <InventoryEditor item={inventoryItem} referenceData={referenceData} />

        <aside className="space-y-6">
          <PhotoManager id={inventoryItem.id} photos={photos} />
          <ListingGenerator item={inventoryItem} />
          <StatusControl id={inventoryItem.id} status={inventoryItem.status} />

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Financial snapshot</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3 text-sm">
              <div className="flex justify-between gap-4"><span className="text-muted-foreground">Acquisition cost</span><span className="font-medium tabular-nums">{money(cost)}</span></div>
              {inventoryItem.acquisitionLotId && (
                <div className="flex justify-between gap-4">
                  <span className="text-muted-foreground">Acquisition lot</span>
                  <Link href="/purchases" className="font-medium text-brand-700 hover:underline">{inventoryItem.acquisitionLotName ?? "View lot"}</Link>
                </div>
              )}
              <div className="flex justify-between gap-4"><span className="text-muted-foreground">Expected sale</span><span className="font-medium tabular-nums">{money(inventoryItem.estimatedSalePrice)}</span></div>
              <div className="flex justify-between gap-4 border-t pt-3"><span className="text-muted-foreground">Projected gross profit</span><span className="font-medium tabular-nums">{money(projectedProfit)}</span></div>
              <div className="flex justify-between gap-4"><span className="inline-flex items-center gap-1 text-muted-foreground"><TrendingUp className="h-3.5 w-3.5" /> Projected ROI</span><span className="font-medium tabular-nums">{projectedRoi == null ? "—" : `${projectedRoi.toFixed(1)}%`}</span></div>
            </CardContent>
          </Card>

          {inventoryItem.isBundle && (inventoryItem.bundleComponents?.length ?? 0) > 0 && (
            <Card>
              <CardHeader>
                <CardTitle className="text-lg">Bundle components</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3 text-sm">
                {inventoryItem.bundleComponents!.map((component) => (
                  <div key={component.id} className="flex items-start justify-between gap-4 border-b pb-3 last:border-0 last:pb-0">
                    <div>
                      <Link href={`/inventory/${component.componentInventoryItemId}`} className="font-medium hover:underline">
                        {component.componentTitle}
                      </Link>
                      <p className="text-xs text-muted-foreground">{inventoryStatusLabel(component.componentStatus)}</p>
                    </div>
                    <span className="tabular-nums">{money(component.costSnapshot)}</span>
                  </div>
                ))}
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Record metadata</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3 text-sm">
              <div className="flex items-start gap-2"><Barcode className="mt-0.5 h-4 w-4 text-muted-foreground" /><div><p className="text-xs text-muted-foreground">Barcode / SKU</p><p>{inventoryItem.barcode ?? "Not assigned"}</p></div></div>
              <div className="flex items-start gap-2"><MapPin className="mt-0.5 h-4 w-4 text-muted-foreground" /><div><p className="text-xs text-muted-foreground">Storage</p><p>{inventoryItem.storageLocation ?? "Not assigned"}</p></div></div>
              <div className="flex items-start gap-2"><CalendarDays className="mt-0.5 h-4 w-4 text-muted-foreground" /><div><p className="text-xs text-muted-foreground">Created</p><p>{date(inventoryItem.createdAt)}</p></div></div>
            </CardContent>
          </Card>
        </aside>
      </div>
    </div>
  );
}
