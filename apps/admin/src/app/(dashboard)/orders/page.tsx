import Link from "next/link";
import { PackageCheck, ShoppingBag, Truck } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Card } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";
const date = (value: string | null | undefined) =>
  value ? new Intl.DateTimeFormat("en-US", { dateStyle: "medium" }).format(new Date(value)) : "—";

const orderStatusVariant = (status: string | undefined) => {
  if (status === "Delivered") return "success" as const;
  if (["Cancelled", "Refunded"].includes(status ?? "")) return "destructive" as const;
  if (status === "Pending") return "warning" as const;
  if (["Paid", "Fulfilling", "Shipped"].includes(status ?? "")) return "info" as const;
  return "secondary" as const;
};

const FILTERS = [
  "All",
  "Pending",
  "Paid",
  "Fulfilling",
  "Shipped",
  "Delivered",
  "Cancelled",
  "Refunded",
] as const;

type OrderSummaryWithTracking = components["schemas"]["OrderSummaryDto"] & {
  shippingCarrier?: string | null;
  trackingNumber?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
};

export default async function OrdersPage({
  searchParams,
}: {
  searchParams: Promise<{ status?: string }>;
}) {
  const { status } = await searchParams;
  const active = FILTERS.includes((status ?? "All") as (typeof FILTERS)[number])
    ? (status ?? "All")
    : "All";

  const api = await getApiClient();
  const { data } = await api.GET("/api/orders", {
    params: { query: active === "All" ? {} : { status: active } },
  });
  const orders = (data ?? []) as OrderSummaryWithTracking[];

  const openOrders = orders.filter(
    (order) => !["Delivered", "Cancelled", "Refunded"].includes(order.status ?? "")
  );
  const revenue = orders
    .filter((order) => order.paymentStatus === "Paid")
    .reduce((sum, order) => sum + (order.grandTotal ?? 0), 0);
  const awaitingFulfillment = orders.filter(
    (order) =>
      order.fulfillmentStatus !== "Fulfilled" && ["Paid", "Fulfilling"].includes(order.status ?? "")
  ).length;

  const kpis = [
    {
      label: "Open orders",
      value: String(openOrders.length),
      detail: "Not delivered, cancelled, or refunded",
      icon: ShoppingBag,
    },
    {
      label: "Captured revenue",
      value: money(revenue),
      detail: "Paid orders in view",
      icon: PackageCheck,
    },
    {
      label: "Awaiting fulfillment",
      value: String(awaitingFulfillment),
      detail: "Paid, not yet shipped",
      icon: Truck,
    },
  ];

  return (
    <div className="space-y-6">
      <header>
        <p className="text-muted-foreground text-sm">Commerce</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Orders</h1>
        <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
          Every channel order with payment and fulfillment state. Open one to inspect the lines,
          tracking, buyer, and reconciled sale financials.
        </p>
      </header>

      <div className="grid gap-4 sm:grid-cols-3">
        {kpis.map((kpi) => (
          <Card key={kpi.label} className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-muted-foreground text-sm">{kpi.label}</span>
              <kpi.icon className="text-muted-foreground h-4 w-4" aria-hidden />
            </div>
            <p className="mt-3 text-3xl font-semibold tabular-nums">{kpi.value}</p>
            <p className="text-muted-foreground mt-1 text-xs">{kpi.detail}</p>
          </Card>
        ))}
      </div>

      <div className="flex flex-wrap gap-2">
        {FILTERS.map((filter) => (
          <Link
            key={filter}
            href={filter === "All" ? "/orders" : `/orders?status=${filter}`}
            className={`rounded-full border px-3 py-1 text-sm transition ${
              active === filter
                ? "bg-foreground text-background border-foreground"
                : "text-muted-foreground hover:bg-muted"
            }`}
          >
            {filter}
          </Link>
        ))}
      </div>

      {orders.length === 0 ? (
        <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
          No orders{active === "All" ? " yet" : ` with status “${active}”`}.
        </div>
      ) : (
        <div className="bg-card overflow-x-auto rounded-xl border">
          <table className="w-full text-sm">
            <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
              <tr>
                <th className="px-4 py-3 font-medium">Order</th>
                <th className="px-4 py-3 font-medium">Channel</th>
                <th className="px-4 py-3 font-medium">Customer</th>
                <th className="px-4 py-3 font-medium">Placed</th>
                <th className="px-4 py-3 text-right font-medium">Items</th>
                <th className="px-4 py-3 text-right font-medium">Total</th>
                <th className="px-4 py-3 font-medium">Tracking</th>
                <th className="px-4 py-3 font-medium">Payment</th>
                <th className="px-4 py-3 font-medium">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {orders.map((order) => (
                <tr key={order.id} className="hover:bg-muted/30 transition-colors">
                  <td className="p-0">
                    <Link
                      href={`/orders/${order.id}`}
                      className="text-foreground block px-4 py-3 font-medium"
                    >
                      {order.orderNumber}
                      {order.isLocalPickup && (
                        <span className="text-muted-foreground ml-2 text-xs">· pickup</span>
                      )}
                    </Link>
                  </td>
                  <td className="px-4 py-3">
                    <span className="bg-muted rounded-md px-2 py-0.5 text-xs font-medium">
                      {order.channel ?? "Storefront"}
                    </span>
                  </td>
                  {/* Marketplace buyers have a username, not an email. */}
                  <td className="text-muted-foreground px-4 py-3">
                    {order.customerName || order.buyerReference || order.email || "—"}
                  </td>
                  <td className="text-muted-foreground px-4 py-3">{date(order.createdAt)}</td>
                  <td className="px-4 py-3 text-right tabular-nums">{order.itemCount}</td>
                  <td className="px-4 py-3 text-right font-medium tabular-nums">
                    {money(order.grandTotal)}
                  </td>
                  <td className="text-muted-foreground px-4 py-3">
                    {order.isLocalPickup ? (
                      "Pickup"
                    ) : order.trackingNumber ? (
                      <span>
                        <span className="text-foreground font-medium">
                          {order.shippingCarrier ?? "Carrier"}
                        </span>
                        <span className="block max-w-[180px] truncate text-xs">
                          {order.trackingNumber}
                        </span>
                      </span>
                    ) : order.shippedAt ? (
                      <span>Shipped {date(order.shippedAt)}</span>
                    ) : (
                      "—"
                    )}
                  </td>
                  <td className="px-4 py-3">
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
                  </td>
                  <td className="px-4 py-3">
                    <Badge variant={orderStatusVariant(order.status)}>{order.status}</Badge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
