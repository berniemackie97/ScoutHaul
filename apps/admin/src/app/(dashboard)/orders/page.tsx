import Link from "next/link";
import { PackageCheck, Plus, ShoppingBag, Truck } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Card, formatCurrency } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";
import { OrdersTable } from "./orders-table";

export const dynamic = "force-dynamic";


/**
 * Queues, not statuses. The operator's question is "what do I do next", and the answer is the stage
 * OrderWorkflow derived — the raw status enum never said whether an order was waiting on a person.
 * "Needs attention" leads because it is the one that costs money to ignore.
 */
const QUEUES = [
  { key: "All", label: "All" },
  { key: "NeedsAttention", label: "Needs attention" },
  { key: "AwaitingPayment", label: "Awaiting payment" },
  { key: "ToPack", label: "To pack" },
  { key: "ToShip", label: "To ship" },
  { key: "ReadyForPickup", label: "Ready for pickup" },
  { key: "InTransit", label: "In transit" },
  { key: "Delivered", label: "Delivered" },
  { key: "Closed", label: "Closed" },
] as const;
type QueueKey = (typeof QUEUES)[number]["key"];

type OrderSummaryWithTracking = components["schemas"]["OrderSummaryDto"];

export default async function OrdersPage({
  searchParams,
}: {
  searchParams: Promise<{ stage?: string }>;
}) {
  const { stage } = await searchParams;
  const active: QueueKey = QUEUES.some((queue) => queue.key === stage)
    ? (stage as QueueKey)
    : "All";

  // The stage is derived, not stored, so it cannot be a server-side filter without duplicating the
  // rule in SQL. The list is capped at 200 rows, so filtering here costs nothing and keeps one
  // definition of each queue.
  const api = await getApiClient();
  const { data } = await api.GET("/api/orders", { params: { query: {} } });
  const allOrders = (data ?? []) as OrderSummaryWithTracking[];
  const orders = active === "All" ? allOrders : allOrders.filter((order) => order.stage === active);
  const countFor = (queue: QueueKey) =>
    queue === "All" ? allOrders.length : allOrders.filter((order) => order.stage === queue).length;

  const revenue = allOrders
    .filter((order) => order.paymentStatus === "Paid")
    .reduce((sum, order) => sum + (order.grandTotal ?? 0), 0);
  const openWork = countFor("ToPack") + countFor("ToShip") + countFor("ReadyForPickup");
  const needsAttention = countFor("NeedsAttention");

  const kpis = [
    {
      label: "Work waiting",
      value: String(openWork),
      detail: "To pack, to ship, or ready for pickup",
      icon: Truck,
      href: "/orders?stage=ToPack",
    },
    {
      label: "Needs attention",
      value: String(needsAttention),
      detail: "A return in flight or a payment problem",
      icon: ShoppingBag,
      href: "/orders?stage=NeedsAttention",
    },
    {
      label: "Captured revenue",
      value: formatCurrency(revenue),
      detail: "Paid orders",
      icon: PackageCheck,
      href: undefined,
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
        {kpis.map((kpi) => {
          const body = (
            <>
              <div className="flex items-center justify-between">
                <span className="text-muted-foreground text-sm">{kpi.label}</span>
                <kpi.icon className="text-muted-foreground h-4 w-4" aria-hidden />
              </div>
              <p className="mt-3 text-3xl font-semibold tabular-nums">{kpi.value}</p>
              <p className="text-muted-foreground mt-1 text-xs">{kpi.detail}</p>
            </>
          );
          // A count you cannot click is a dead end — each one opens the queue it counts.
          return kpi.href ? (
            <Link key={kpi.label} href={kpi.href} className="rounded-xl">
              <Card className="hover:border-foreground/20 h-full p-5 transition">{body}</Card>
            </Link>
          ) : (
            <Card key={kpi.label} className="p-5">
              {body}
            </Card>
          );
        })}
      </div>

      <div className="flex flex-wrap gap-2">
        {QUEUES.map((queue) => {
          const count = countFor(queue.key);
          if (count === 0 && queue.key !== active && queue.key !== "All") return null;
          return (
            <Link
              key={queue.key}
              href={queue.key === "All" ? "/orders" : `/orders?stage=${queue.key}`}
              className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-sm transition ${
                active === queue.key
                  ? "bg-foreground text-background border-foreground"
                  : "text-muted-foreground hover:bg-muted"
              }`}
            >
              {queue.label}
              <span className="tabular-nums opacity-70">{count}</span>
            </Link>
          );
        })}
      </div>

      <OrdersTable
        orders={orders}
        emptyLabel={
          active === "All" ? "No orders yet." : `Nothing in “${QUEUES.find((queue) => queue.key === active)?.label}”.`
        }
      />
    </div>
  );
}
