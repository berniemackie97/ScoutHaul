import Link from "next/link";
import { Card } from "@overhaul/ui";
import { Radio, Store, Globe, AlertTriangle } from "lucide-react";
import { getApiClient } from "@/lib/api";
import { ChannelManager } from "./channel-manager";
import { refusal } from "@/lib/page-access";

export const dynamic = "force-dynamic";

export default async function ChannelsPage() {
  const refused = await refusal("/channels");
  if (refused) return refused;

  const api = await getApiClient();
  const [{ data: accounts }, { data: reference }, { data: delist }, { data: sold }] = await Promise.all([
    api.GET("/api/channel-accounts", {}),
    api.GET("/api/sales/reference-data", {}),
    api.GET("/api/channel-listings", { params: { query: { status: "PendingDelist" } } }),
    api.GET("/api/platform-listings/sold-unrecorded", {}),
  ]);

  const list = accounts ?? [];
  const pendingDelist = delist ?? [];
  const soldNotRecorded = sold ?? [];
  const nativeCount = list.filter((account) => account.isNativelySynced && account.isActive).length;
  const externalCount = list.filter((account) => !account.isNativelySynced && account.isActive).length;

  const kpis = [
    { label: "Active channels", value: String(list.filter((a) => a.isActive).length), detail: "Connected and in use", icon: Radio },
    { label: "Native", value: String(nativeCount), detail: "Storefront — synced by the app", icon: Store },
    { label: "External", value: String(externalCount), detail: "Marketplaces — manually tracked", icon: Globe },
  ];

  return (
    <div className="space-y-6">
      <header>
        <p className="text-muted-foreground text-sm">Commerce</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Channels</h1>
        <p className="text-muted-foreground mt-1 max-w-3xl text-sm">
          The accounts your inventory can be tracked against. Your storefront is native; external marketplaces are recorded manually from
          reports, admin actions, and the listing helper while you work in your own browser.
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

      {pendingDelist.length > 0 && (
        <Card className="border-amber-300 p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <div className="flex items-center gap-2 text-sm font-semibold text-amber-700">
                <AlertTriangle className="h-4 w-4" /> Still for sale after selling ({pendingDelist.length})
              </div>
              <p className="text-muted-foreground mt-1 text-xs">
                Sold on one marketplace and still live on another, where they can be bought again.
              </p>
            </div>
            <Link
              href="/channels/delist"
              className="hover:bg-muted inline-flex h-9 shrink-0 items-center rounded-md border px-3 text-sm font-medium"
            >
              Work through them
            </Link>
          </div>
        </Card>
      )}

      {soldNotRecorded.length > 0 && (
        <Card className="border-amber-300 p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <div className="flex items-center gap-2 text-sm font-semibold text-amber-700">
                <AlertTriangle className="h-4 w-4" /> Sold, but not on the books ({soldNotRecorded.length})
              </div>
              <p className="text-muted-foreground mt-1 text-xs">
                A marketplace says these sold and no sale here accounts for the money, and no order for them has
                arrived yet.
              </p>
            </div>
            <Link
              href="/channels/sold"
              className="hover:bg-muted inline-flex h-9 shrink-0 items-center rounded-md border px-3 text-sm font-medium"
            >
              Record them
            </Link>
          </div>
        </Card>
      )}

      <ChannelManager accounts={list} channels={reference?.channels ?? []} />
    </div>
  );
}
