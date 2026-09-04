import Link from "next/link";
import { ArrowUpRight, Boxes, CircleDollarSign, PackageOpen, Tags, TrendingUp, TriangleAlert, Wallet } from "lucide-react";
import { Card, formatCurrency } from "@overhaul/ui";
import { getApiClient } from "@/lib/api";

export const dynamic = "force-dynamic";

// Whole dollars on the tiles, and $0 rather than a dash: an aggregate of no sales really is nought.
const money = (value: number | null | undefined) =>
  formatCurrency(value ?? 0, { maximumFractionDigits: 0 });

export default async function DashboardPage() {
  const api = await getApiClient();
  const [sessionResult, summaryResult] = await Promise.all([
    api.GET("/api/session/bootstrap", {}),
    api.GET("/api/dashboard/summary", {}),
  ]);
  const session = sessionResult.data;
  const summary = summaryResult.data;
  const org = session?.organizations?.find((organization) => organization.id === session.selectedOrganizationId)
    ?? session?.organizations?.[0];

  const kpis = [
    { label: "Inventory at cost", value: money(summary?.totalInventoryCost), detail: `${summary?.currentInventoryCount ?? 0} active items`, icon: Boxes },
    { label: "Expected inventory value", value: money(summary?.estimatedInventoryValue), detail: `${summary?.activeListingsCount ?? 0} active listings`, icon: TrendingUp },
    { label: "Gross sales", value: money(summary?.grossSales), detail: "Recorded sales, all time", icon: CircleDollarSign },
    { label: "Net profit", value: money(summary?.netProfit), detail: "After recorded item costs and fees", icon: Wallet },
  ];

  const projectedSpread = (summary?.estimatedInventoryValue ?? 0) - (summary?.totalInventoryCost ?? 0);

  return (
    <div className="space-y-8">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
        <div>
          <p className="text-sm text-muted-foreground">{org?.name ?? "Operator workspace"}</p>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight">Command center</h1>
          <p className="mt-1 text-sm text-muted-foreground">Live operating position from inventory, listings, and realized sales.</p>
        </div>
        <p className="text-xs text-muted-foreground">Signed in as {session?.user?.displayName ?? session?.user?.email ?? "operator"}</p>
      </header>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {kpis.map((kpi) => (
          <Card key={kpi.label} className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">{kpi.label}</span>
              <kpi.icon className="h-4 w-4 text-muted-foreground" aria-hidden />
            </div>
            <p className="mt-3 text-3xl font-semibold tabular-nums">{kpi.value}</p>
            <p className="mt-1 text-xs text-muted-foreground">{kpi.detail}</p>
          </Card>
        ))}
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.4fr)_minmax(300px,0.6fr)]">
        <Card className="p-6">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-sm font-medium">Inventory position</p>
              <p className="mt-1 text-sm text-muted-foreground">Capital deployed and expected gross spread before selling costs.</p>
            </div>
            <PackageOpen className="h-5 w-5 text-muted-foreground" />
          </div>
          <div className="mt-6 grid gap-5 sm:grid-cols-3">
            <div><p className="text-xs uppercase tracking-wide text-muted-foreground">Cash deployed</p><p className="mt-1 text-xl font-semibold tabular-nums">{money(summary?.cashDeployed)}</p></div>
            <div><p className="text-xs uppercase tracking-wide text-muted-foreground">Expected value</p><p className="mt-1 text-xl font-semibold tabular-nums">{money(summary?.estimatedInventoryValue)}</p></div>
            <div><p className="text-xs uppercase tracking-wide text-muted-foreground">Projected spread</p><p className="mt-1 text-xl font-semibold tabular-nums">{money(projectedSpread)}</p></div>
          </div>
        </Card>

        <Card className="p-6">
          <div className="flex items-center justify-between">
            <div>
              <p className="text-sm font-medium">Action queue</p>
              <p className="mt-1 text-xs text-muted-foreground">Inventory requiring operator attention.</p>
            </div>
            <TriangleAlert className="h-5 w-5 text-amber-600" />
          </div>
          <div className="mt-5 space-y-3">
            <Link href="/inventory?attention=Unlisted" className="flex items-center justify-between rounded-lg border p-3 transition-colors hover:bg-muted/40">
              <span className="inline-flex items-center gap-2 text-sm"><Tags className="h-4 w-4 text-muted-foreground" /> Not listed</span>
              <span className="inline-flex items-center gap-2 font-semibold tabular-nums">{summary?.unlistedInventoryCount ?? 0}<ArrowUpRight className="h-3.5 w-3.5" /></span>
            </Link>
            <Link href="/inventory?attention=Aged" className="flex items-center justify-between rounded-lg border p-3 transition-colors hover:bg-muted/40">
              <span className="inline-flex items-center gap-2 text-sm"><TriangleAlert className="h-4 w-4 text-muted-foreground" /> Aged over 60 days</span>
              <span className="inline-flex items-center gap-2 font-semibold tabular-nums">{summary?.staleInventoryCount ?? 0}<ArrowUpRight className="h-3.5 w-3.5" /></span>
            </Link>
          </div>
        </Card>
      </div>
    </div>
  );
}
