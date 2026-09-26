import Link from "next/link";
import { Handshake, Package, Tag } from "lucide-react";
import { Badge, Card, formatCurrency } from "@overhaul/ui";
import { ListEmpty, ListSearch, PageHeader } from "@/components/list/list-search";
import { Pager } from "@/components/list/pager";
import { StatCard, StatRow } from "@/components/list/stat-card";
import { inventoryStatusLabel } from "@/features/inventory/inventory-operations";
import { getApiClient } from "@/lib/api";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

/**
 * Stock held on someone else's behalf.
 *
 * This page used to pull every inventory item the list endpoint would return — capped at 200 — keep
 * the consigned ones, and sum their prices into "Asking value" and "Estimated resale". Past 200
 * items both figures were the total of a window, and the list below them was missing rows.
 */
export default async function ConsignmentsPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const refused = await refusal("/consignments");
  if (refused) return refused;

  const query = await searchParams;
  const search = typeof query.q === "string" ? query.q.trim() : "";
  const page = Math.max(1, Number(query.page) || 1);

  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory/browse", {
    params: {
      query: {
        includeArchived: false,
        ownership: "Consigned",
        search: search || undefined,
        sort: "updatedAt",
        desc: true,
        page,
        pageSize: 25,
      },
    },
  });

  const items = data?.items ?? [];
  const summary = data?.summary;
  const total = data?.total ?? 0;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Sourcing"
        title="Consignments"
        description="Inventory you sell on someone else's behalf. Set an item's ownership to Consigned on its record to track it here."
      />

      <StatRow>
        <StatCard label="Consigned items" value={String(total)} detail="Owned by a consignor" icon={Handshake} />
        <StatCard
          label="Asking value"
          value={formatCurrency(summary?.askingValue ?? 0)}
          detail="Sum of consignor asks, all consigned stock"
          icon={Tag}
        />
        <StatCard
          label="Estimated resale"
          value={formatCurrency(summary?.estimatedResaleValue ?? 0)}
          detail="Projected sale value, falling back to the ask"
          icon={Package}
        />
      </StatRow>

      <ListSearch defaultValue={search} placeholder="Search title, brand, category, barcode, or location" />

      {total === 0 ? (
        <ListEmpty>
          {search ? (
            "No consigned inventory matches that search."
          ) : (
            <>
              No consigned inventory. Set an item&rsquo;s ownership type to <strong>Consigned</strong> to surface it here.
            </>
          )}
        </ListEmpty>
      ) : (
        <>
          <Card className="overflow-x-auto p-0">
            <table className="w-full text-sm">
              <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
                <tr>
                  <th className="px-4 py-3 font-medium">Item</th>
                  <th className="px-4 py-3 font-medium">Stage</th>
                  <th className="px-4 py-3 text-right font-medium">Consignor ask</th>
                  <th className="px-4 py-3 text-right font-medium">Est. resale</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {items.map((item) => (
                  <tr key={item.id} className="hover:bg-muted/30">
                    <td className="p-0">
                      <Link href={`/inventory/${item.id}`} className="text-foreground block px-4 py-3 font-medium">
                        {item.title}
                        <span className="text-muted-foreground block text-xs">
                          {[item.category, item.brand, item.storageLocation].filter(Boolean).join(" · ") ||
                            "No classification"}
                        </span>
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <Badge variant="secondary">{inventoryStatusLabel(item.status)}</Badge>
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">{formatCurrency(item.sellerAskingPrice)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{formatCurrency(item.estimatedSalePrice)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
          <Pager page={data?.page ?? 1} pageCount={data?.pageCount ?? 1} total={total} noun="item" />
        </>
      )}
    </div>
  );
}
