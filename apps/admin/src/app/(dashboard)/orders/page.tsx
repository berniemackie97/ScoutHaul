import Link from "next/link";
import { PackageCheck, Plus, ShoppingBag, Truck } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Card } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { OrdersTable } from "./orders-table";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number"
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value)
    : "—";

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
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
        <div>
          <p className="text-muted-foreground text-sm">Commerce</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Orders</h1>
          <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
            The operational surface for every channel order — lines, fees, refunds, and fulfillment.
            Open one to manage its sale financials and tracking.
          </p>
        </div>
        <div className="flex shrink-0 flex-wrap gap-2">
          <Link
            href="/orders/new"
            className="bg-brand-600 hover:bg-brand-700 inline-flex h-10 items-center justify-center gap-2 rounded-lg px-4 text-sm font-medium text-white"
          >
            <Plus className="h-4 w-4" /> Record sale
          </Link>
          <Link
            href="/orders/bundled"
            className="hover:bg-muted inline-flex h-10 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium"
          >
            <Plus className="h-4 w-4" /> Bundled sale
          </Link>
        </div>
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

      <OrdersTable
        orders={orders}
        emptyLabel={`No orders${active === "All" ? " yet" : ` with status “${active}”`}.`}
      />
    </div>
  );
}
