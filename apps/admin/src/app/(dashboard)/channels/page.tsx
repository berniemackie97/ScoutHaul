import { Card } from "@overhaul/ui";
import { Radio, Store, Globe } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { ChannelManager } from "./channel-manager";

export const dynamic = "force-dynamic";

export default async function ChannelsPage() {
  const api = await getApiClient();
  const [{ data: accounts }, { data: reference }] = await Promise.all([
    api.GET("/api/channel-accounts", {}),
    api.GET("/api/sales/reference-data", {}),
  ]);

  const list = accounts ?? [];
  const nativeCount = list.filter((account) => account.isNativelySynced && account.isActive).length;
  const externalCount = list.filter((account) => !account.isNativelySynced && account.isActive).length;

  const kpis = [
    { label: "Active channels", value: String(list.filter((a) => a.isActive).length), detail: "Connected and in use", icon: Radio },
    { label: "Native", value: String(nativeCount), detail: "Storefront — synced by the app", icon: Store },
    { label: "External", value: String(externalCount), detail: "Marketplaces — extension-bridged", icon: Globe },
  ];

  return (
    <div className="space-y-6">
      <header>
        <p className="text-muted-foreground text-sm">Commerce</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Channels</h1>
        <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
          The accounts your inventory lists on. Your storefront syncs natively; external marketplaces (eBay, Poshmark, Depop, Vinted…) are
          bridged by the human-led cross-listing extension. Connect an account here so items can be cross-listed and kept in sync.
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

      <ChannelManager accounts={list} channels={reference?.channels ?? []} />
    </div>
  );
}
