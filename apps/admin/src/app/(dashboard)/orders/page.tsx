import Link from "next/link";
import { PackageCheck, Plus, ShoppingBag, Truck } from "lucide-react";
import { formatCurrency } from "@overhaul/ui";
import { ListSearch, PageHeader } from "@/components/list/list-search";
import { Pager } from "@/components/list/pager";
import { StatCard, StatRow } from "@/components/list/stat-card";
import { ORDER_STAGES } from "@/features/orders/order-stages";
import { getApiClient } from "@/lib/api";
import { OrdersTable } from "./orders-table";

export const dynamic = "force-dynamic";


/**
 * Queues, not statuses. The operator's question is "what do I do next", and the answer is the stage
 * OrderWorkflow derived — the raw status enum never said whether an order was waiting on a person.
 * "Needs attention" leads because it is the one that costs money to ignore.
 */
const QUEUES = [{ key: "All", label: "All" }, ...ORDER_STAGES] as const;
type QueueKey = (typeof QUEUES)[number]["key"];

/** Switching queue keeps the search but drops the page — page 4 of a different queue is usually empty. */
function queueHref(queue: QueueKey, search: string) {
  const params = new URLSearchParams();
  if (queue !== "All") params.set("stage", queue);
  if (search) params.set("q", search);
  const query = params.toString();
  return query ? `/orders?${query}` : "/orders";
}

export default async function OrdersPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const query = await searchParams;
  const stage = typeof query.stage === "string" ? query.stage : undefined;
  const search = typeof query.q === "string" ? query.q.trim() : "";
  const page = Math.max(1, Number(query.page) || 1);
  const active: QueueKey = QUEUES.some((queue) => queue.key === stage) ? (stage as QueueKey) : "All";

  // The queue counts and the revenue come back with the page and span the whole order book. This
  // used to fetch the newest 200 orders and bucket them in the browser, so every figure on the page
  // silently became a figure about the recent past once the book passed 200.
  const api = await getApiClient();
  const { data } = await api.GET("/api/orders/browse", {
    params: {
      query: {
        stage: active === "All" ? undefined : active,
        search: search || undefined,
        page,
        pageSize: 25,
      },
    },
  });

  const orders = data?.items ?? [];
  const summary = data?.summary;
  const countFor = (queue: QueueKey) =>
    queue === "All"
      ? (summary?.totalOrders ?? 0)
      : (summary?.stages?.find((entry) => entry.stage === queue)?.count ?? 0);

  const revenue = summary?.capturedRevenue ?? 0;
  const openWork = summary?.openWork ?? 0;
  const needsAttention = summary?.needsAttention ?? 0;

  const kpis = [
    {
      label: "Work waiting",
      value: String(openWork),
      detail: "To pack, to ship, or ready for pickup — all orders",
      icon: Truck,
      href: "/orders?stage=ToPack",
    },
    {
      label: "Needs attention",
      value: String(needsAttention),
      detail: "A return in flight or a payment problem — all orders",
      icon: ShoppingBag,
      href: "/orders?stage=NeedsAttention",
    },
    {
      label: "Captured revenue",
      value: formatCurrency(revenue),
      detail: "Every paid order, all time",
      icon: PackageCheck,
      href: undefined,
    },
  ];

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Commerce"
        title="Orders"
        description="The operational surface for every channel order — lines, fees, refunds, and fulfillment. Open one to manage its sale financials and tracking."
        action={
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
        }
      />

      <StatRow>
        {kpis.map((kpi) => (
          <StatCard
            key={kpi.label}
            label={kpi.label}
            value={kpi.value}
            detail={kpi.detail}
            icon={kpi.icon}
            href={kpi.href}
            tone={kpi.label === "Needs attention" && needsAttention > 0 ? "warning" : "default"}
          />
        ))}
      </StatRow>

      <div className="flex flex-wrap gap-2">
        {QUEUES.map((queue) => {
          const count = countFor(queue.key);
          if (count === 0 && queue.key !== active && queue.key !== "All") return null;
          return (
            <Link
              key={queue.key}
              href={queueHref(queue.key, search)}
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

      <ListSearch defaultValue={search} placeholder="Search order number, email, buyer reference, or marketplace id">
        {active === "All" ? null : <input type="hidden" name="stage" value={active} />}
      </ListSearch>

      <OrdersTable
        orders={orders}
        emptyLabel={
          search
            ? "No orders match that search."
            : active === "All"
              ? "No orders yet."
              : `Nothing in “${QUEUES.find((queue) => queue.key === active)?.label}”.`
        }
      />

      <Pager page={data?.page ?? 1} pageCount={data?.pageCount ?? 1} total={data?.total ?? 0} noun="order" />
    </div>
  );
}
