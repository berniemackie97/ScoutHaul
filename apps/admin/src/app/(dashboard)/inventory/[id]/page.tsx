import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, Barcode, CalendarDays, MapPin, TrendingUp } from "lucide-react";
import { Badge, Card, CardContent, CardHeader, CardTitle } from "@overhaul/ui";
import {
  getInventoryCost,
  getProjectedMetrics,
  inventoryStatusLabel,
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
  if (!item?.id || !item.status) notFound();
  if (!referenceData) throw new Error("Inventory reference data could not be loaded.");

  const photos: ManagedPhoto[] = (item.photos ?? []).flatMap((photo) => {
    const imageUrl = getApiAssetUrl(photo.thumbnailUrl);
    if (!photo.id || !photo.originalUrl || !imageUrl) return [];
    return [{ id: photo.id, imageUrl, apiUrl: photo.originalUrl, isHero: photo.isHero ?? false }];
  });

  const cost = getInventoryCost(item);
  const { profit: projectedProfit, roiPercent: projectedRoi } = getProjectedMetrics(item);

  return (
    <div className="space-y-6">
      <header className="space-y-4">
        <Link href="/inventory" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Inventory
        </Link>
        <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
          <div>
            <div className="mb-2 flex flex-wrap items-center gap-2">
              <Badge variant="secondary">{inventoryStatusLabel(item.status)}</Badge>
              {item.category && <Badge variant="outline">{item.category}</Badge>}
            </div>
            <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">{item.title}</h1>
            <p className="mt-1 text-sm text-muted-foreground">Canonical inventory record · updated {date(item.updatedAt)}</p>
          </div>
          <div className="font-mono text-xs text-muted-foreground">{item.id}</div>
        </div>
      </header>

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_340px]">
        <InventoryEditor item={item} referenceData={referenceData} />

        <aside className="space-y-6">
          <PhotoManager id={item.id} photos={photos} />
          <ListingGenerator item={item} />
          <StatusControl id={item.id} status={item.status} />

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Financial snapshot</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3 text-sm">
              <div className="flex justify-between gap-4"><span className="text-muted-foreground">Acquisition cost</span><span className="font-medium tabular-nums">{money(cost)}</span></div>
              <div className="flex justify-between gap-4"><span className="text-muted-foreground">Expected sale</span><span className="font-medium tabular-nums">{money(item.estimatedSalePrice)}</span></div>
              <div className="flex justify-between gap-4 border-t pt-3"><span className="text-muted-foreground">Projected gross profit</span><span className="font-medium tabular-nums">{money(projectedProfit)}</span></div>
              <div className="flex justify-between gap-4"><span className="inline-flex items-center gap-1 text-muted-foreground"><TrendingUp className="h-3.5 w-3.5" /> Projected ROI</span><span className="font-medium tabular-nums">{projectedRoi == null ? "—" : `${projectedRoi.toFixed(1)}%`}</span></div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Record metadata</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3 text-sm">
              <div className="flex items-start gap-2"><Barcode className="mt-0.5 h-4 w-4 text-muted-foreground" /><div><p className="text-xs text-muted-foreground">Barcode / SKU</p><p>{item.barcode ?? "Not assigned"}</p></div></div>
              <div className="flex items-start gap-2"><MapPin className="mt-0.5 h-4 w-4 text-muted-foreground" /><div><p className="text-xs text-muted-foreground">Storage</p><p>{item.storageLocation ?? "Not assigned"}</p></div></div>
              <div className="flex items-start gap-2"><CalendarDays className="mt-0.5 h-4 w-4 text-muted-foreground" /><div><p className="text-xs text-muted-foreground">Created</p><p>{date(item.createdAt)}</p></div></div>
            </CardContent>
          </Card>
        </aside>
      </div>
    </div>
  );
}
