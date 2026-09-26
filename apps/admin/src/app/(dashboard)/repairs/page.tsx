import Link from "next/link";
import { AlertTriangle, UserCog, Wrench } from "lucide-react";
import { Badge, Card, formatCurrency } from "@overhaul/ui";
import { ListEmpty, ListSearch, PageHeader } from "@/components/list/list-search";
import { Pager } from "@/components/list/pager";
import { StatCard, StatRow } from "@/components/list/stat-card";
import { getApiClient } from "@/lib/api";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

const functionalLabel: Record<string, string> = {
  Untested: "Untested",
  FunctionalWithIssues: "Functional w/ issues",
  Intermittent: "Intermittent",
  NonFunctional: "Non-functional",
  PartsOnly: "Parts only",
};

const functionalVariant = (status: string | null | undefined) => {
  if (status === "NonFunctional") return "destructive" as const;
  if (status === "FunctionalWithIssues" || status === "Intermittent") return "warning" as const;
  return "secondary" as const;
};

/**
 * The bench queue.
 *
 * This page used to fetch every inventory item the list endpoint would return — capped at 200 — and
 * keep the ones needing work. Past 200 items the queue silently dropped rows and the counts above it
 * were a count of that window. The server now answers "what needs bench work" directly, so the queue
 * is the whole queue and the figures span all of it.
 */
export default async function RepairsPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const refused = await refusal("/repairs");
  if (refused) return refused;

  const query = await searchParams;
  const search = typeof query.q === "string" ? query.q.trim() : "";
  const page = Math.max(1, Number(query.page) || 1);

  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory/browse", {
    params: {
      query: {
        includeArchived: false,
        needsBenchWork: true,
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
        eyebrow="Operations"
        title="Repairs"
        description="The bench queue — inventory that is untested or not fully functional, plus items a customer owns and left here to be worked on."
      />

      <StatRow>
        <StatCard label="In repair queue" value={String(summary?.needsBenchWork ?? 0)} detail="Across all inventory" icon={Wrench} />
        <StatCard label="Customer repairs" value={String(summary?.customerOwned ?? 0)} detail="Owned by a customer" icon={UserCog} />
        <StatCard
          label="Non-functional"
          value={String(summary?.nonFunctional ?? 0)}
          detail="Confirmed not working"
          icon={AlertTriangle}
          tone={(summary?.nonFunctional ?? 0) > 0 ? "warning" : "default"}
        />
      </StatRow>

      <ListSearch defaultValue={search} placeholder="Search title, brand, category, barcode, or location" />

      {total === 0 ? (
        <ListEmpty>
          {search
            ? "Nothing in the repair queue matches that search."
            : "Nothing in the repair queue. Items marked untested or not fully functional appear here."}
        </ListEmpty>
      ) : (
        <>
          <Card className="overflow-x-auto p-0">
            <table className="w-full text-sm">
              <thead className="bg-muted/40 text-muted-foreground border-b text-left text-xs uppercase tracking-wide">
                <tr>
                  <th className="px-4 py-3 font-medium">Item</th>
                  <th className="px-4 py-3 font-medium">Condition</th>
                  <th className="px-4 py-3 font-medium">Ownership</th>
                  <th className="px-4 py-3 text-right font-medium">Est. resale</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {items.map((item) => (
                  <tr key={item.id} className="hover:bg-muted/30">
                    <td className="p-0">
                      <Link href={`/inventory/${item.id}`} className="text-foreground block px-4 py-3 font-medium">
                        {item.title}
                        {item.testingNotes ? (
                          <span className="text-muted-foreground block text-xs">{item.testingNotes}</span>
                        ) : null}
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <Badge variant={functionalVariant(item.functionalStatus)}>
                        {functionalLabel[item.functionalStatus ?? ""] ?? item.functionalStatus}
                      </Badge>
                    </td>
                    <td className="text-muted-foreground px-4 py-3">
                      {item.ownershipType === "CustomerRepair" ? "Customer" : "Own stock"}
                    </td>
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
