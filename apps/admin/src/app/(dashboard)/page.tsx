import { getApiClient } from "@/lib/api";
import { Card } from "@overhaul/ui";
import { Boxes, Tags, Wallet, ClipboardList } from "lucide-react";

export const dynamic = "force-dynamic";

export default async function DashboardPage() {
  const api = await getApiClient();
  const { data } = await api.GET("/api/session/bootstrap", {});
  const counts = data?.remoteCounts;
  const org =
    data?.organizations?.find((o) => o.id === data?.selectedOrganizationId) ?? data?.organizations?.[0];

  const kpis = [
    { label: "Inventory items", value: counts?.inventoryItems ?? 0, icon: Boxes },
    { label: "Listings", value: counts?.listings ?? 0, icon: Tags },
    { label: "Budgets", value: counts?.budgets ?? 0, icon: Wallet },
    { label: "Market notes", value: counts?.marketObservations ?? 0, icon: ClipboardList },
  ];

  return (
    <div className="space-y-8">
      <header>
        <p className="text-sm text-muted-foreground">{org?.name ?? "—"}</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Command center</h1>
      </header>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {kpis.map((k) => (
          <Card key={k.label} className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">{k.label}</span>
              <k.icon className="h-4 w-4 text-muted-foreground" aria-hidden />
            </div>
            <p className="mt-3 text-3xl font-semibold tabular-nums">{k.value.toLocaleString()}</p>
          </Card>
        ))}
      </div>

      <p className="text-sm text-muted-foreground">
        Live from your HaulScout inventory. Inventory management, listings, photos, statuses and finances
        are being built out here next.
      </p>
    </div>
  );
}
