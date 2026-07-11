import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Card, CardContent, CardHeader, CardTitle } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { OrderStatusControl } from "./order-status-control";
import { OrderTrackingCard } from "./order-tracking-card";
import { OrderDeleteButton } from "./order-delete-button";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";
const dateTime = (value: string | null | undefined) =>
  value
    ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium", timeStyle: "short" }).format(
        new Date(value)
      )
    : "—";

const orderStatusVariant = (status: string | undefined) => {
  if (status === "Delivered") return "success" as const;
  if (["Cancelled", "Refunded"].includes(status ?? "")) return "destructive" as const;
  if (status === "Pending") return "warning" as const;
  if (["Paid", "Fulfilling", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};
const saleStatusVariant = (status: string | undefined) => {
  if (status === "Delivered") return "success" as const;
  if (["Returned", "Refunded", "Cancelled"].includes(status ?? "")) return "destructive" as const;
  if (["ReturnRequested", "PartiallyRefunded"].includes(status ?? "")) return "warning" as const;
  if (["Packed", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

type OrderStatus =
  | "Pending"
  | "Paid"
  | "Fulfilling"
  | "Shipped"
  | "Delivered"
  | "Cancelled"
  | "Refunded";

type OrderWithTracking = components["schemas"]["OrderDetailDto"] & {
  shippingCarrier?: string | null;
  trackingNumber?: string | null;
  trackingUrl?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
};
type OrderLineSale = NonNullable<NonNullable<OrderWithTracking["lines"]>[number]["sale"]>;
type SaleFinancials = {
  itemRevenue: number;
  buyerShipping: number;
  marketplaceFees: number;
  sellerShipping: number;
  suppliesAndOther: number;
  costOfGoods: number;
  refunded: number;
  salesTaxOwed: number;
  netProfit: number;
};

export default async function OrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const api = await getApiClient();
  const { data: order } = await api.GET("/api/orders/{id}", { params: { path: { id } } });
  if (!order) notFound();
  const trackedOrder = order as OrderWithTracking;

  const lines = order.lines ?? [];
  const saleLines = lines
    .map((line) => line.sale)
    .filter((sale): sale is OrderLineSale => Boolean(sale));
  const saleFinancials = saleLines.reduce<SaleFinancials>(
    (total, sale) => ({
      itemRevenue: total.itemRevenue + (sale.soldPrice ?? 0),
      buyerShipping: total.buyerShipping + (sale.shippingCharged ?? 0),
      marketplaceFees: total.marketplaceFees + (sale.platformFees ?? 0) + (sale.paymentFees ?? 0),
      sellerShipping: total.sellerShipping + (sale.shippingCost ?? 0),
      suppliesAndOther: total.suppliesAndOther + (sale.suppliesCost ?? 0) + (sale.otherCosts ?? 0),
      costOfGoods: total.costOfGoods + (sale.costOfGoods ?? 0),
      refunded: total.refunded + (sale.totalRefunded ?? 0),
      salesTaxOwed: total.salesTaxOwed + (sale.salesTaxOwed ?? 0),
      netProfit: total.netProfit + (sale.netProfit ?? 0),
    }),
    {
      itemRevenue: 0,
      buyerShipping: 0,
      marketplaceFees: 0,
      sellerShipping: 0,
      suppliesAndOther: 0,
      costOfGoods: 0,
      refunded: 0,
      salesTaxOwed: 0,
      netProfit: 0,
    }
  );
  const shipTo = [
    order.shipToLine1,
    order.shipToLine2,
    [order.shipToCity, order.shipToRegion, order.shipToPostalCode].filter(Boolean).join(", "),
    order.shipToCountry,
  ]
    .filter(Boolean)
    .join("\n");
  const totals: Array<[string, number | null | undefined, boolean?]> = [
    ["Subtotal", order.subtotal],
    ["Discounts", order.discountTotal != null ? -order.discountTotal : null],
    ["Tax", order.taxTotal],
    ["Shipping", order.shippingTotal],
    ["Total", order.grandTotal, true],
  ];

  return (
    <div className="space-y-6">
      <div>
        <Link
          href="/orders"
          className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm"
        >
          <ArrowLeft className="h-4 w-4" /> Orders
        </Link>
        <div className="mt-2 flex flex-col justify-between gap-3 sm:flex-row sm:items-start">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">{order.orderNumber}</h1>
            <p className="text-muted-foreground mt-1 text-sm">Placed {dateTime(order.createdAt)}</p>
          </div>
          <div className="flex flex-col gap-2 sm:items-end">
            <div className="flex flex-wrap gap-2">
              <Badge variant="outline">{order.channel ?? "Storefront"}</Badge>
              {order.externalOrderId && <Badge variant="outline">{order.externalOrderId}</Badge>}
              <Badge variant={orderStatusVariant(order.status)}>{order.status}</Badge>
              <Badge
                variant={
                  order.paymentStatus === "Paid"
                    ? "success"
                    : order.paymentStatus === "Refunded"
                      ? "destructive"
                      : "secondary"
                }
              >
                {order.paymentStatus}
              </Badge>
              <Badge variant={order.fulfillmentStatus === "Fulfilled" ? "success" : "secondary"}>
                {order.fulfillmentStatus}
              </Badge>
            </div>
            <OrderDeleteButton id={order.id!} orderNumber={order.orderNumber ?? "this order"} />
          </div>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Line items</CardTitle>
            </CardHeader>
            <CardContent className="p-0">
              <table className="w-full text-sm">
                <thead className="bg-muted/40 text-muted-foreground border-y text-left text-xs uppercase tracking-wide">
                  <tr>
                    <th className="px-4 py-2 font-medium">Item</th>
                    <th className="px-4 py-2 font-medium">Sale</th>
                    <th className="px-4 py-2 text-right font-medium">Item</th>
                    <th className="px-4 py-2 text-right font-medium">Buyer ship</th>
                    <th className="px-4 py-2 text-right font-medium">Fees</th>
                    <th className="px-4 py-2 text-right font-medium">COGS</th>
                    <th className="px-4 py-2 text-right font-medium">Refunded</th>
                    <th className="px-4 py-2 text-right font-medium">Qty</th>
                    <th className="px-4 py-2 text-right font-medium">True net</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {lines.map((line) => {
                    const sale = line.sale;
                    const fees = (sale?.platformFees ?? 0) + (sale?.paymentFees ?? 0);
                    const refunded = sale?.totalRefunded ?? 0;
                    return (
                      <tr key={line.id}>
                        <td className="px-4 py-3">
                          <Link
                            href={`/inventory/${line.inventoryItemId}`}
                            className="font-medium hover:underline"
                          >
                            {line.titleSnapshot}
                          </Link>
                          {line.skuSnapshot && (
                            <span className="text-muted-foreground block text-xs">
                              SKU {line.skuSnapshot}
                            </span>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          {sale?.id ? (
                            <div className="space-y-1">
                              <Badge variant={saleStatusVariant(sale.status)}>{sale.status}</Badge>
                              <Link
                                href={`/finance/${sale.id}`}
                                className="text-brand-700 block text-xs hover:underline"
                              >
                                Correct sale financials
                              </Link>
                            </div>
                          ) : (
                            <span className="text-muted-foreground text-xs">No sale line</span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums">
                          {money(line.unitPrice)}
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums">
                          {money(sale?.shippingCharged)}
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums">
                          {sale ? (fees > 0 ? `-${money(fees)}` : money(0)) : "—"}
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums">
                          {money(sale?.costOfGoods ?? line.unitCostSnapshot)}
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums">
                          {refunded > 0 ? (
                            <span className="text-amber-700">-{money(refunded)}</span>
                          ) : (
                            <span className="text-muted-foreground text-xs">—</span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-right tabular-nums">{line.quantity}</td>
                        <td
                          className={`px-4 py-3 text-right font-medium tabular-nums ${(sale?.netProfit ?? 0) < 0 ? "text-destructive" : ""}`}
                        >
                          {money(sale?.netProfit)}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-lg">Payment &amp; exceptions</CardTitle>
            </CardHeader>
            <CardContent>
              <OrderStatusControl
                id={order.id!}
                status={(order.status ?? "Pending") as OrderStatus}
              />
            </CardContent>
          </Card>

          <OrderTrackingCard
            order={{
              id: order.id!,
              isLocalPickup: order.isLocalPickup,
              shippingCarrier: trackedOrder.shippingCarrier,
              trackingNumber: trackedOrder.trackingNumber,
              trackingUrl: trackedOrder.trackingUrl,
              shippedAt: trackedOrder.shippedAt,
              deliveredAt: trackedOrder.deliveredAt,
            }}
          />
        </div>

        <aside className="space-y-4">
          <Card className="p-5">
            <p className="text-muted-foreground text-sm">Order total</p>
            <p className="mt-1 text-3xl font-semibold tabular-nums">{money(order.grandTotal)}</p>
            <div className="mt-4 space-y-1 border-t pt-3 text-sm">
              {totals.map(([label, value, strong]) => (
                <div
                  key={label}
                  className={`flex justify-between ${strong ? "border-t pt-2 font-semibold" : ""}`}
                >
                  <span className="text-muted-foreground">{label}</span>
                  <span className="tabular-nums">{money(value)}</span>
                </div>
              ))}
              <div className="text-muted-foreground flex justify-between pt-2 text-xs">
                <span>Merchandise margin (ex tax/ship)</span>
                <span className="tabular-nums">{money(order.grossMargin)}</span>
              </div>
            </div>
          </Card>

          <Card className="p-5 text-sm">
            <p className="text-muted-foreground text-xs uppercase tracking-wide">Sale financials</p>
            {saleLines.length > 0 ? (
              <div className="mt-3 space-y-1">
                <FinancialRow label="Item revenue" value={saleFinancials.itemRevenue} />
                <FinancialRow label="Buyer shipping charged" value={saleFinancials.buyerShipping} />
                <FinancialRow
                  label="Marketplace/payment fees"
                  value={saleFinancials.marketplaceFees}
                  negative
                />
                <FinancialRow
                  label="Seller shipping labels"
                  value={saleFinancials.sellerShipping}
                  negative
                />
                <FinancialRow
                  label="Supplies + other costs"
                  value={saleFinancials.suppliesAndOther}
                  negative
                />
                <FinancialRow label="Cost of goods" value={saleFinancials.costOfGoods} negative />
                <FinancialRow
                  label="Refunds issued"
                  value={saleFinancials.refunded}
                  negative
                  muted={saleFinancials.refunded === 0}
                />
                <FinancialRow
                  label="Sales tax owed"
                  value={saleFinancials.salesTaxOwed}
                  negative
                  muted={saleFinancials.salesTaxOwed === 0}
                />
                <FinancialRow label="True net profit" value={saleFinancials.netProfit} strong />
              </div>
            ) : (
              <p className="text-muted-foreground mt-2">
                No reconciled sale financials are attached to this order yet.
              </p>
            )}
          </Card>

          <Card className="p-5 text-sm">
            <p className="text-muted-foreground text-xs uppercase tracking-wide">Customer</p>
            <p className="mt-1 font-medium">
              {order.customerName || order.buyerReference || order.email || "Unknown buyer"}
            </p>
            <p className="text-muted-foreground">
              {order.email ||
                (order.buyerReference ? `${order.channel} buyer` : "No email on file")}
            </p>
            {order.customerId && (
              <Link
                href={`/customers/${order.customerId}`}
                className="text-brand-700 mt-2 inline-block text-sm hover:underline"
              >
                View customer
              </Link>
            )}
          </Card>

          <Card className="p-5 text-sm">
            <p className="text-muted-foreground text-xs uppercase tracking-wide">
              {order.isLocalPickup ? "Local pickup" : "Ship to"}
            </p>
            {order.isLocalPickup ? (
              <p className="mt-1">Customer is collecting in person.</p>
            ) : shipTo ? (
              <>
                {order.shipToName && <p className="mt-1 font-medium">{order.shipToName}</p>}
                <p className="text-muted-foreground whitespace-pre-line">{shipTo}</p>
              </>
            ) : (
              <p className="text-muted-foreground mt-1">No shipping address on file.</p>
            )}
            {order.customerNote && (
              <p className="text-muted-foreground mt-3 border-t pt-3 italic">
                “{order.customerNote}”
              </p>
            )}
          </Card>

          {(trackedOrder.trackingNumber || trackedOrder.shippedAt || trackedOrder.deliveredAt) && (
            <Card className="p-5 text-sm">
              <p className="text-muted-foreground text-xs uppercase tracking-wide">Tracking</p>
              <p className="mt-1 font-medium">
                {[trackedOrder.shippingCarrier, trackedOrder.trackingNumber]
                  .filter(Boolean)
                  .join(" · ") || "Tracking recorded"}
              </p>
              {trackedOrder.trackingUrl && (
                <Link
                  href={trackedOrder.trackingUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="text-brand-700 mt-2 inline-block hover:underline"
                >
                  Open carrier tracking
                </Link>
              )}
              <div className="text-muted-foreground mt-3 space-y-1 border-t pt-3">
                <p>Shipped: {dateTime(trackedOrder.shippedAt)}</p>
                <p>Delivered: {dateTime(trackedOrder.deliveredAt)}</p>
              </div>
            </Card>
          )}
        </aside>
      </div>
    </div>
  );
}

function FinancialRow({
  label,
  value,
  negative = false,
  strong = false,
  muted = false,
}: {
  label: string;
  value: number;
  negative?: boolean;
  strong?: boolean;
  muted?: boolean;
}) {
  return (
    <div className={`flex justify-between gap-4 ${strong ? "border-t pt-2 font-semibold" : ""}`}>
      <span className={muted ? "text-muted-foreground/70" : "text-muted-foreground"}>{label}</span>
      <span className={`tabular-nums ${value < 0 ? "text-destructive" : ""}`}>
        {negative && value > 0 ? "-" : ""}
        {money(value)}
      </span>
    </div>
  );
}
