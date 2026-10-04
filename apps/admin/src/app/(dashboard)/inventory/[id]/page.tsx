import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, ImageIcon } from "lucide-react";
import { Badge, Card, CardContent, CardHeader, CardTitle, formatCurrency } from "@overhaul/ui";
import { getInventoryCost, inventoryStatusLabel, type InventoryItem } from "@/features/inventory/inventory-operations";
import { findStatusConflicts } from "@/features/inventory/status-conflicts";
import { getApiAssetUrl, getApiClient } from "@/lib/api";
import { InventoryEditor } from "./inventory-editor";
import { PhotoManager, type ManagedPhoto } from "./photo-manager";
import { StatusControl } from "./status-control";
import { ListingGenerator } from "./listing-generator";
import { ChannelPresencePanel } from "./cross-list-panel";
import { RecordSaleCard } from "./record-sale-card";
import { DeleteInventoryCard } from "./delete-inventory-card";
import { StorefrontPolicyCard } from "./storefront-policy-card";
import { StatusConflictNotice } from "./status-conflict-notice";
import { refusal } from "@/lib/page-access";
import { When } from "@/components/when";
import { ItemFacts } from "./item-facts";

export const dynamic = "force-dynamic";



export default async function InventoryDetailPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const refused = await refusal("/inventory");
  if (refused) return refused;

  const { id } = await params;
  // Set when an operator arrives from a marketplace listing that sold, so the sale card opens knowing what it can.
  const query = await searchParams;
  const one = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);
  const soldOnPlatform = one(query.soldOn);
  const prefill = soldOnPlatform
    ? { platform: soldOnPlatform, price: one(query.price), soldOn: one(query.soldAt) }
    : undefined;
  const api = await getApiClient();
  const [
    { data: item },
    { data: referenceData },
    { data: channelAccounts },
    { data: channelListings },
    { data: salesReference },
    { data: storeListing },
    { data: itemSales },
  ] = await Promise.all([
    api.GET("/api/inventory/{id}", { params: { path: { id } } }),
    api.GET("/api/inventory/reference-data"),
    api.GET("/api/channel-accounts", {}),
    api.GET("/api/inventory/{itemId}/channel-listings", { params: { path: { itemId: id } } }),
    api.GET("/api/sales/reference-data", {}),
    api.GET("/api/store/listings/by-item/{inventoryItemId}", { params: { path: { inventoryItemId: id } } }),
    api.GET("/api/sales/by-item/{inventoryItemId}", { params: { path: { inventoryItemId: id } } }),
  ]);
  // The sale that made this item sold — the newest one that still stands.
  const sale = (itemSales ?? []).find((entry) => entry.status !== "Cancelled");
  const inventoryItem = item as InventoryItem | undefined;
  if (!inventoryItem?.id || !inventoryItem.status) notFound();
  if (!referenceData) throw new Error("Inventory reference data could not be loaded.");

  const photos: ManagedPhoto[] = (inventoryItem.photos ?? []).flatMap((photo) => {
    const imageUrl = getApiAssetUrl(photo.thumbnailUrl);
    if (!photo.id || !photo.originalUrl || !imageUrl) return [];
    return [{ id: photo.id, imageUrl, apiUrl: photo.originalUrl, isHero: photo.isHero ?? false }];
  });

  // The status control and the channel panel below both write this item's state and neither reads
  // the other, so say plainly when they contradict each other.
  const statusConflicts = findStatusConflicts(inventoryItem.status, channelListings ?? []);

  const cost = getInventoryCost(inventoryItem);
  const hero = photos.find((photo) => photo.isHero) ?? photos[0];

  return (
    <div className="space-y-6">
      <header className="space-y-4">
        <Link href="/inventory" className="text-muted-foreground hover:text-foreground inline-flex items-center gap-2 text-sm">
          <ArrowLeft className="h-4 w-4" /> Inventory
        </Link>
        <div className="flex items-start gap-4">
          <div className="bg-muted hidden h-20 w-20 shrink-0 items-center justify-center overflow-hidden rounded-lg border sm:flex">
            {hero ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img src={hero.imageUrl} alt="" className="h-full w-full object-cover" />
            ) : (
              <ImageIcon className="text-muted-foreground h-6 w-6" aria-hidden />
            )}
          </div>
          <div className="min-w-0">
            <div className="mb-2 flex flex-wrap items-center gap-2">
              <Badge variant="secondary">{inventoryStatusLabel(inventoryItem.status)}</Badge>
              {inventoryItem.isBundle && <Badge variant="info">Bundle</Badge>}
              {inventoryItem.category && <Badge variant="outline">{inventoryItem.category}</Badge>}
            </div>
            <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">{inventoryItem.title}</h1>
            <p className="text-muted-foreground mt-1 text-sm">
              Added <When value={inventoryItem.createdAt} /> · updated <When value={inventoryItem.updatedAt} />
            </p>
          </div>
        </div>
      </header>

      <ItemFacts item={inventoryItem} sale={sale} listings={channelListings ?? []} />

      <StatusConflictNotice conflicts={statusConflicts} />

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_360px]">
        <InventoryEditor item={inventoryItem} referenceData={referenceData} />

        <aside className="space-y-6">
          <StatusControl item={inventoryItem} sale={sale} />
          {!inventoryItem.isSold && (
            <RecordSaleCard itemId={inventoryItem.id} cost={cost ?? 0} channels={salesReference?.channels ?? []} prefill={prefill} />
          )}
          <ChannelPresencePanel
            itemId={inventoryItem.id}
            accounts={(channelAccounts ?? []).filter((account) => account.isActive)}
            listings={channelListings ?? []}
          />
          <PhotoManager id={inventoryItem.id} photos={photos} />
          <StorefrontPolicyCard itemId={inventoryItem.id} listing={storeListing} />
          <ListingGenerator item={inventoryItem} />

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
                      <p className="text-muted-foreground text-xs">{inventoryStatusLabel(component.componentStatus)}</p>
                    </div>
                    <span className="tabular-nums">{formatCurrency(component.costSnapshot)}</span>
                  </div>
                ))}
              </CardContent>
            </Card>
          )}

          <DeleteInventoryCard id={inventoryItem.id} title={inventoryItem.title ?? ""} />
        </aside>
      </div>
    </div>
  );
}
