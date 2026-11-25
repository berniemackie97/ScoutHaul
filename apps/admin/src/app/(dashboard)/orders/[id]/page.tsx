import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft, ExternalLink } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Card, formatCurrency } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { When } from "@/components/when";
import { paymentNote, stageLabel, stageVariant } from "@/features/orders/order-presentation";
import { OrderFulfillment } from "./order-fulfillment";
import { OrderActionsMenu } from "./order-actions-menu";
import { LineItems } from "./line-items";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

type OrderLineSale = NonNullable<NonNullable<components["schemas"]["OrderDetailDto"]["lines"]>[number]["sale"]>;
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
  const refused = await refusal("/orders");
  if (refused) return refused;

  const { id } = await params;
  const api = await getApiClient();
  const { data: order } = await api.GET("/api/orders/{id}", { params: { path: { id } } });
  if (!order) notFound();

  const lines = order.lines ?? [];
  const saleLines = lines
    .map((line) => line.sale)
    .filter((sale): sale is OrderLineSale => Boolean(sale));

  // Refunds, returns and corrections all live on the sale line; fetch them per line so the whole sale can be
  // managed inline on the order (refund, return, correct fees/price, history) instead of on a separate buried page.
  type Refund = components["schemas"]["RefundDto"];
  type ReturnDto = components["schemas"]["ReturnDto"];
  type Correction = components["schemas"]["SaleCorrectionDto"];
  const [{ data: reference }, perLine] = await Promise.all([
    api.GET("/api/sales/reference-data"),
    Promise.all(
      saleLines.map(async (sale) => {
        const [{ data: returns }, { data: refunds }, { data: corrections }] = await Promise.all([
          api.GET("/api/sales/{id}/returns", { params: { path: { id: sale.id! } } }),
          api.GET("/api/sales/{id}/refunds", { params: { path: { id: sale.id! } } }),
          api.GET("/api/sales/{id}/corrections", { params: { path: { id: sale.id! } } }),
        ]);
        return { id: sale.id!, returns: returns ?? [], refunds: refunds ?? [], corrections: corrections ?? [] };
      }),
    ),
  ]);
  const channels = reference?.channels ?? [];
  const returnsBySale: Record<string, ReturnDto[]> = Object.fromEntries(
    perLine.map((entry) => [entry.id, entry.returns]),
  );
  const refundsBySale: Record<string, Refund[]> = Object.fromEntries(
    perLine.map((entry) => [entry.id, entry.refunds]),
  );
  const correctionsBySale: Record<string, Correction[]> = Object.fromEntries(
    perLine.map((entry) => [entry.id, entry.corrections]),
  );
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

  const channel = order.channel ?? "Storefront";
  const stage = order.workflow?.stage;
  const payment = paymentNote(order.paymentStatus);
  const buyer = order.customerName || order.buyerReference || order.email || "Unknown buyer";
  const itemCount = lines.reduce((sum, line) => sum + (line.quantity ?? 0), 0);
  const externalRef = order.externalOrderId && order.externalOrderId !== order.orderNumber ? order.externalOrderId : null;
  const salesRevenue = saleFinancials.itemRevenue + saleFinancials.buyerShipping;
  const margin = salesRevenue > 0 ? (saleFinancials.netProfit / salesRevenue) * 100 : null;

  return (
    <div className="space-y-6">
      <div>
        <Link href="/orders" className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm">
          <ArrowLeft className="h-4 w-4" /> Orders
        </Link>
        <div className="mt-3 flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
          <div className="min-w-0 space-y-1">
            <div className="text-muted-foreground flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
              <Badge variant="outline">{channel}</Badge>
              <span>
                Placed <When value={order.createdAt} time />
              </span>
              {externalRef && <span className="font-mono text-xs">· {channel} #{externalRef}</span>}
            </div>
            <h1 className="truncate text-2xl font-semibold tracking-tight">Order {order.orderNumber}</h1>
            <p className="text-muted-foreground text-sm">
              {buyer} · {itemCount} item{itemCount === 1 ? "" : "s"}
            </p>
          </div>
          <div className="flex shrink-0 flex-wrap items-start gap-2 sm:justify-end">
            {stage && <Badge variant={stageVariant(stage)} className="h-9 px-3 text-sm">{stageLabel(stage)}</Badge>}
            {payment && <Badge variant={payment.variant} className="h-9 px-3 text-sm">{payment.label}</Badge>}
            {order.externalOrderUrl && (
              <a
                href={order.externalOrderUrl}
                target="_blank"
                rel="noreferrer"
                className="hover:bg-muted inline-flex h-9 items-center gap-1.5 rounded-md border px-3 text-sm font-medium"
              >
                Open on {channel} <ExternalLink className="h-3.5 w-3.5" aria-hidden />
              </a>
            )}
            <OrderActionsMenu
              id={order.id!}
              orderNumber={order.orderNumber ?? "this order"}
              canCancel={order.workflow?.availableActions?.includes("Cancel") ?? false}
            />
          </div>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_340px]">
        <div className="min-w-0 space-y-6">
          <OrderFulfillment
            order={{
              id: order.id!,
              channel,
              isLocalPickup: order.isLocalPickup ?? false,
              paymentStatus: order.paymentStatus,
              createdAt: order.createdAt,
              paidAt: order.paidAt,
              packedAt: order.packedAt,
              shippedAt: order.shippedAt,
              deliveredAt: order.deliveredAt,
              cancelledAt: order.cancelledAt,
              shippingCarrier: order.shippingCarrier,
              trackingNumber: order.trackingNumber,
              trackingUrl: order.trackingUrl,
              trackingDeliveredAt: order.trackingDeliveredAt,
              workflow: order.workflow,
            }}
          />

          <Card>
            <div className="flex flex-col gap-1 p-6 pb-4">
              <h2 className="text-lg font-semibold">Line items</h2>
              <p className="text-muted-foreground text-sm">Refund, run a return, or correct fees from a line’s menu.</p>
            </div>
            <LineItems
              lines={lines}
              orderId={id}
              channels={channels}
              refundsBySale={refundsBySale}
              returnsBySale={returnsBySale}
              correctionsBySale={correctionsBySale}
            />
          </Card>
        </div>

        <aside className="space-y-4">
          <Card className="p-5 text-sm">
            <div className="flex items-baseline justify-between">
              <p className="text-muted-foreground text-xs uppercase tracking-wide">Buyer paid</p>
              <p className="text-2xl font-semibold tabular-nums">{formatCurrency(order.grandTotal)}</p>
            </div>
            <div className="mt-3 space-y-1">
              <MoneyRow label="Items" value={order.subtotal ?? 0} />
              <MoneyRow label="Shipping" value={order.shippingTotal ?? 0} />
              {(order.taxTotal ?? 0) > 0 && <MoneyRow label="Tax collected" value={order.taxTotal ?? 0} />}
              {(order.discountTotal ?? 0) > 0 && <MoneyRow label="Discounts" value={order.discountTotal ?? 0} negative />}
              {(order.totalRefunded ?? 0) > 0 && (
                <>
                  <MoneyRow label="Refunded" value={order.totalRefunded ?? 0} negative warn />
                  <MoneyRow label="Kept" value={(order.grandTotal ?? 0) - (order.totalRefunded ?? 0)} strong />
                </>
              )}
            </div>

            {saleLines.length > 0 ? (
              <>
                <p className="text-muted-foreground mt-5 text-xs uppercase tracking-wide">Costs</p>
                <div className="mt-2 space-y-1">
                  <MoneyRow label="Marketplace & payment fees" value={saleFinancials.marketplaceFees} negative />
                  <MoneyRow label="Shipping labels" value={saleFinancials.sellerShipping} negative />
                  {saleFinancials.suppliesAndOther > 0 && (
                    <MoneyRow label="Supplies & other" value={saleFinancials.suppliesAndOther} negative />
                  )}
                  <MoneyRow label="Cost of goods" value={saleFinancials.costOfGoods} negative />
                  {saleFinancials.salesTaxOwed > 0 && (
                    <MoneyRow label="Sales tax owed" value={saleFinancials.salesTaxOwed} negative />
                  )}
                </div>
                <div className="mt-3 flex items-baseline justify-between border-t pt-3">
                  <span className="font-semibold">True net profit</span>
                  <span className={`text-lg font-semibold tabular-nums ${saleFinancials.netProfit < 0 ? "text-destructive" : "text-success"}`}>
                    {formatCurrency(saleFinancials.netProfit)}
                  </span>
                </div>
                {margin != null && (
                  <p className="text-muted-foreground mt-1 text-right text-xs tabular-nums">{margin.toFixed(1)}% margin</p>
                )}
              </>
            ) : (
              <p className="text-muted-foreground mt-4 border-t pt-3 text-xs">No sale is attached to this order yet, so costs and profit are unknown.</p>
            )}
          </Card>

          <Card className="space-y-4 p-5 text-sm">
            <div>
              <p className="text-muted-foreground text-xs uppercase tracking-wide">Customer</p>
              <p className="mt-1 font-medium">{buyer}</p>
              <p className="text-muted-foreground">
                {order.email || (order.buyerReference ? `${channel} buyer` : "No email on file")}
              </p>
              {order.customerId && (
                <Link href={`/customers/${order.customerId}`} className="text-brand-700 mt-1 inline-block hover:underline">
                  View customer
                </Link>
              )}
            </div>
            <div className="border-t pt-4">
              <p className="text-muted-foreground text-xs uppercase tracking-wide">
                {order.isLocalPickup ? "Local pickup" : "Ship to"}
              </p>
              {order.isLocalPickup ? (
                <p className="mt-1">The buyer collects in person.</p>
              ) : shipTo ? (
                <>
                  {order.shipToName && <p className="mt-1 font-medium">{order.shipToName}</p>}
                  <p className="text-muted-foreground whitespace-pre-line">{shipTo}</p>
                </>
              ) : (
                <p className="text-muted-foreground mt-1">
                  {channel === "Storefront" ? "No shipping address on file." : `${channel} keeps the address and prints the label.`}
                </p>
              )}
            </div>
            {order.customerNote && (
              <p className="text-muted-foreground border-t pt-4 italic">“{order.customerNote}”</p>
            )}
          </Card>
        </aside>
      </div>
    </div>
  );
}

function MoneyRow({
  label,
  value,
  negative = false,
  strong = false,
  warn = false,
}: {
  label: string;
  value: number;
  negative?: boolean;
  strong?: boolean;
  warn?: boolean;
}) {
  return (
    <div className={`flex justify-between gap-4 ${strong ? "border-t pt-1.5 font-semibold" : ""} ${warn ? "text-warning" : ""}`}>
      <span className={strong || warn ? "" : "text-muted-foreground"}>{label}</span>
      <span className="tabular-nums whitespace-nowrap">
        {negative && value > 0 ? "−" : ""}
        {formatCurrency(value)}
      </span>
    </div>
  );
}
