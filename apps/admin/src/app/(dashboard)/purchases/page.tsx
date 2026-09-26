import Link from "next/link";
import { ArrowRight, Boxes, Coins, ReceiptText } from "lucide-react";
import { Badge, formatCurrency, formatDate } from "@overhaul/ui";
import { ListEmpty, ListSearch, PageHeader } from "@/components/list/list-search";
import { Pager } from "@/components/list/pager";
import { StatCard, StatRow } from "@/components/list/stat-card";
import { getApiClient } from "@/lib/api";
import { PurchaseLotCreator } from "./purchase-lot-creator";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";



/**
 * Purchase records and their landed cost.
 *
 * The money on this page used to be summed in the browser from whatever lots the list endpoint
 * returned — 200 at most, newest first — and labelled "Total purchase spend". The summary now comes
 * back with the page and covers every lot ever recorded.
 */
export default async function PurchasesPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const refused = await refusal("/purchases");
  if (refused) return refused;

  const query = await searchParams;
  const search = typeof query.q === "string" ? query.q.trim() : "";
  const page = Math.max(1, Number(query.page) || 1);

  const api = await getApiClient();
  const [{ data: lots }, { data: open }] = await Promise.all([
    api.GET("/api/acquisition-lots/browse", {
      params: { query: { search: search || undefined, page, pageSize: 25 } },
    }),
    // Only the count is shown, so one row is asked for; the total is over every item.
    api.GET("/api/inventory/browse", { params: { query: { lifecycle: "Active", lotCandidates: true, page: 1, pageSize: 1 } } }),
  ]);

  const acquisitionLots = lots?.items ?? [];
  const summary = lots?.summary;
  const totalSpend = summary?.totalSpend ?? 0;
  const allocated = summary?.totalAllocated ?? 0;
  const unallocated = summary?.unallocated ?? 0;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Operations"
        title="Purchases & acquisition lots"
        description="Record a single-item purchase or a multi-item receipt, pallet, haul, auction pickup, or loose lot, then allocate landed cost into inventory for accurate COGS and historical profit."
        action={
          <Link href="/inventory" className="hover:bg-muted inline-flex h-10 items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium">
            Inventory <ArrowRight className="h-4 w-4" />
          </Link>
        }
      />

      <StatRow>
        <StatCard
          label="Total purchase spend"
          value={formatCurrency(totalSpend)}
          detail={`Landed cost across all ${summary?.totalLots ?? 0} lots`}
          icon={ReceiptText}
        />
        <StatCard
          label="Allocated COGS"
          value={formatCurrency(allocated)}
          detail={unallocated > 0.009 ? `${formatCurrency(unallocated)} still to allocate` : "Fully allocated"}
          icon={Coins}
          tone={unallocated > 0.009 ? "warning" : "default"}
        />
        <StatCard
          label="Open candidates"
          value={open ? String(open.total ?? 0) : "—"}
          detail="Active items not yet in a lot"
          icon={Boxes}
        />
      </StatRow>

      <PurchaseLotCreator />

      <section className="space-y-3">
        <div>
          <h2 className="text-lg font-semibold">Recorded purchase records</h2>
          <p className="text-sm text-muted-foreground">Each record creates one purchase expense and allocated item-level cost basis.</p>
        </div>
        <ListSearch defaultValue={search} placeholder="Search purchase name, supplier, or reference" />

        {(lots?.total ?? 0) === 0 ? (
          <ListEmpty>{search ? "No purchase records match that search." : "No purchase records recorded yet."}</ListEmpty>
        ) : (
          <div className="overflow-x-auto rounded-xl border bg-card">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/40 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-4 py-3 font-medium">Purchase record</th>
                  <th className="px-4 py-3 font-medium">Purchased</th>
                  <th className="px-4 py-3 font-medium">Allocation</th>
                  <th className="px-4 py-3 text-right font-medium">Items</th>
                  <th className="px-4 py-3 text-right font-medium">Landed cost</th>
                  <th className="px-4 py-3 text-right font-medium">Allocated</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {acquisitionLots.map((lot) => (
                  <tr key={lot.id} className="hover:bg-muted/30">
                    <td className="px-4 py-3">
                      <Link href={`/purchases/${lot.id}`} className="font-medium hover:underline">{lot.name || "Untitled purchase"}</Link>
                      <p className="text-xs text-muted-foreground">{[lot.supplierName, lot.externalReference].filter(Boolean).join(" · ") || "No supplier/reference"}</p>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{formatDate(lot.purchasedAt)}</td>
                    <td className="px-4 py-3"><Badge variant="secondary">{lot.allocationMethod}</Badge></td>
                    <td className="px-4 py-3 text-right tabular-nums">{lot.items?.length ?? 0}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{formatCurrency(lot.totalLandedCost)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{formatCurrency(lot.totalAllocatedCost)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {(lots?.total ?? 0) > 0 ? (
          <Pager page={lots?.page ?? 1} pageCount={lots?.pageCount ?? 1} total={lots?.total ?? 0} noun="purchase record" />
        ) : null}
      </section>
    </div>
  );
}
