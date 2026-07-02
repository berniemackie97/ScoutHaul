import Link from "next/link";
import { Wrench, UserCog, AlertTriangle } from "lucide-react";
import { Badge, Card } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";

export const dynamic = "force-dynamic";

const money = (value: number | null | undefined) =>
  typeof value === "number" ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(value) : "—";

// Functional states that mean the item needs bench work before it can be listed.
const REPAIR_STATES = ["Untested", "FunctionalWithIssues", "Intermittent", "NonFunctional"];
const functionalLabel: Record<string, string> = {
  Untested: "Untested",
  FunctionalWithIssues: "Functional w/ issues",
  Intermittent: "Intermittent",
  NonFunctional: "Non-functional",
};
const functionalVariant = (status: string | undefined) => {
  if (status === "NonFunctional") return "destructive" as const;
  if (["FunctionalWithIssues", "Intermittent"].includes(status ?? "")) return "warning" as const;
  return "secondary" as const;
};

export default async function RepairsPage() {
  const api = await getApiClient();
  const { data } = await api.GET("/api/inventory", { params: { query: { includeArchived: false } } });
  const queue = (data ?? []).filter(
    (item) => item.ownershipType === "CustomerRepair" || REPAIR_STATES.includes(item.functionalStatus ?? "")
  );

  const customerRepairs = queue.filter((item) => item.ownershipType === "CustomerRepair").length;
  const nonFunctional = queue.filter((item) => item.functionalStatus === "NonFunctional").length;

  const kpis = [
    { label: "In repair queue", value: String(queue.length), detail: "Need bench work before listing", icon: Wrench },
    { label: "Customer repairs", value: String(customerRepairs), detail: "Owned by a customer", icon: UserCog },
    { label: "Non-functional", value: String(nonFunctional), detail: "Confirmed not working", icon: AlertTriangle },
  ];

  return (
    <div className="space-y-6">
      <header>
        <p className="text-muted-foreground text-sm">Operations</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Repairs</h1>
        <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
          The bench work queue — inventory that is untested or not fully functional, plus items owned by a customer for repair.
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

      {queue.length === 0 ? (
        <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
          Nothing in the repair queue. Items marked untested or not fully functional appear here.
        </div>
      ) : (
        <div className="bg-card overflow-x-auto rounded-xl border">
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
              {queue.map((item) => (
                <tr key={item.id} className="hover:bg-muted/30">
                  <td className="p-0">
                    <Link href={`/inventory/${item.id}`} className="text-foreground block px-4 py-3 font-medium">
                      {item.title}
                      {item.testingNotes && <span className="text-muted-foreground block text-xs">{item.testingNotes}</span>}
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
                  <td className="px-4 py-3 text-right tabular-nums">{money(item.estimatedSalePrice)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
