import Link from "next/link";
import { Button, Card } from "@overhaul/ui";
import { getApiAssetUrl, getApiClient } from "@/lib/api";
import { refusal } from "@/lib/page-access";
import { PackStation, type PackLine } from "./pack-station";

export const dynamic = "force-dynamic";

/** One order at a time from the pack queue: pick, check, pack. The order named in the address, else the oldest waiting. */
export default async function PackPage({ searchParams }: { searchParams: Promise<{ order?: string }> }) {
  const refused = await refusal("/orders");
  if (refused) return refused;

  const api = await getApiClient();
  const { order: requested } = await searchParams;
  const { data: queue } = await api.GET("/api/orders/browse", { params: { query: { stage: "ToPack", page: 1, pageSize: 50 } } });
  const waiting = [...(queue?.items ?? [])].sort((a, b) => (a.paidAt ?? a.createdAt ?? "").localeCompare(b.paidAt ?? b.createdAt ?? ""));
  const orderId = requested ?? waiting[0]?.id;

  if (!orderId) {
    return (
      <Card className="mx-auto max-w-xl space-y-3 p-8 text-center">
        <h1 className="text-xl font-semibold">Nothing to pack</h1>
        <p className="text-muted-foreground text-sm">Paid orders land here as they come in.</p>
        <Button asChild variant="outline">
          <Link href="/work">Back to Work</Link>
        </Button>
      </Card>
    );
  }

  const { data: order } = await api.GET("/api/orders/{id}", { params: { path: { id: orderId } } });
  if (!order) {
    return <Card className="p-6 text-sm">That order could not be loaded.</Card>;
  }

  const lines: PackLine[] = await Promise.all(
    (order.lines ?? []).map(async (line) => {
      const { data: item } = await api.GET("/api/inventory/{id}", { params: { path: { id: line.inventoryItemId! } } });
      const hero = item?.photos?.find((photo) => photo.isHero) ?? item?.photos?.[0];
      return {
        lineId: line.id!,
        itemId: line.inventoryItemId!,
        title: line.titleSnapshot ?? item?.title ?? "Item",
        sku: line.skuSnapshot ?? null,
        barcode: item?.barcode ?? null,
        bin: item?.storageLocation ?? null,
        condition: item?.condition ?? null,
        flaws: item?.missingParts ?? null,
        included: item?.accessories ?? null,
        quantity: line.quantity ?? 1,
        photoUrl: getApiAssetUrl(hero?.thumbnailUrl),
      };
    }),
  );

  const position = waiting.findIndex((entry) => entry.id === order.id);
  const next = waiting.find((entry) => entry.id !== order.id && (position < 0 || waiting.indexOf(entry) > position)) ?? null;

  return (
    <PackStation
      key={order.id}
      order={{
        id: order.id!,
        number: order.externalOrderId ?? order.orderNumber ?? "",
        channel: order.channel ?? "Storefront",
        buyer: order.customerName ?? order.buyerReference ?? null,
        shipTo: [order.shipToCity, order.shipToRegion].filter(Boolean).join(", ") || null,
        note: order.customerNote ?? null,
        localPickup: order.isLocalPickup ?? false,
        total: order.grandTotal ?? 0,
      }}
      lines={lines}
      run={{ position: position < 0 ? 1 : position + 1, total: Math.max(waiting.length, 1), nextOrderId: next?.id ?? null }}
    />
  );
}
